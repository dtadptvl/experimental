using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: KiloSessionSmoke <KiloSessionHost.exe>");
    return 2;
}

var hostPath = Path.GetFullPath(args[0]);
if (!File.Exists(hostPath))
{
    Console.Error.WriteLine($"Host not found: {hostPath}");
    return 2;
}

var instance = Guid.NewGuid().ToString("N");
var pipeName = $"kilo-manager-smoke-{instance}";
var configPath = Path.Combine(Path.GetTempPath(), $"kilo-host-{instance}.json");
var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
var config = new
{
    projectId = "smoke-project",
    instanceId = instance,
    pipeName,
    workingDirectory = Path.GetTempPath(),
    executable = powerShell,
    arguments = "-NoLogo -NoProfile -NoExit -Command Write-Output KM_SMOKE_READY",
    ringBufferBytes = 1024 * 1024
};
await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

using var host = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = hostPath,
        Arguments = $"--config \"{configPath}\"",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    }
};

try
{
    Console.WriteLine("[start] Launching SessionHost");
    if (!host.Start()) throw new InvalidOperationException("Process.Start returned false.");
    var stdoutTask = host.StandardOutput.ReadToEndAsync();
    var stderrTask = host.StandardError.ReadToEndAsync();

    Console.WriteLine($"[attach-1] Connecting to {pipeName}");
    var first = await ConnectAsync(pipeName, TimeSpan.FromSeconds(10));
    await using (first.Pipe)
    using (first.Reader)
    using (first.Writer)
    {
        Console.WriteLine("[attach-1] Connected");
        var hello = await ReadMessageAsync(first.Reader, "attach-1/hello");
        var snapshot = await ReadMessageAsync(first.Reader, "attach-1/snapshot");
        var status = await ReadMessageAsync(first.Reader, "attach-1/status");
        var initialText = Encoding.UTF8.GetString(Convert.FromBase64String(snapshot.GetProperty("data").GetString() ?? ""));
        Console.WriteLine($"[attach-1] hello={hello}");
        Console.WriteLine($"[attach-1] status={status}");
        Console.WriteLine($"[attach-1] snapshot={initialText.Replace("\r", "\\r").Replace("\n", "\\n")}");
        Require(hello.GetProperty("type").GetString() == "hello", "Invalid hello message.");
        Require(hello.GetProperty("projectId").GetString() == "smoke-project", "Wrong project id.");
        Require(hello.GetProperty("instanceId").GetString() == instance, "Wrong instance id.");
        Require(snapshot.GetProperty("type").GetString() == "snapshot", "Missing initial snapshot.");
        Require(status.GetProperty("type").GetString() == "status" && status.GetProperty("running").GetBoolean(), "Host not running.");
        Console.WriteLine("[attach-1] Handshake OK");

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes("Write-Output KM_SMOKE_FIRST\r\n"));
        await first.Writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "input", data = token }));
        Console.WriteLine("[io] Sent terminal input");

        var found = false;
        using var ioTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!found && !ioTimeout.IsCancellationRequested)
        {
            var message = await ReadMessageAsync(first.Reader, "io/output", ioTimeout.Token);
            if (message.GetProperty("type").GetString() != "output" || !message.TryGetProperty("data", out var data)) continue;
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(data.GetString() ?? ""));
            if (text.Contains("KM_SMOKE_FIRST", StringComparison.Ordinal)) found = true;
        }
        Require(found, "ConPTY output did not contain the smoke token.");
        Console.WriteLine("[io] ConPTY input/output OK");
    }

    await Task.Delay(300);
    Require(!host.HasExited, "SessionHost exited when first client disconnected.");
    Console.WriteLine("[detach] Host survived client disconnect");

    Console.WriteLine("[attach-2] Reconnecting");
    var second = await ConnectAsync(pipeName, TimeSpan.FromSeconds(10));
    await using (second.Pipe)
    using (second.Reader)
    using (second.Writer)
    {
        var hello2 = await ReadMessageAsync(second.Reader, "attach-2/hello");
        var snapshot2 = await ReadMessageAsync(second.Reader, "attach-2/snapshot");
        var status2 = await ReadMessageAsync(second.Reader, "attach-2/status");
        Require(hello2.GetProperty("type").GetString() == "hello", "Reconnect hello missing.");
        Require(status2.GetProperty("running").GetBoolean(), "Reconnect host status not running.");
        var snapshotText = Encoding.UTF8.GetString(Convert.FromBase64String(snapshot2.GetProperty("data").GetString() ?? ""));
        Require(snapshotText.Contains("KM_SMOKE_FIRST", StringComparison.Ordinal), "Reconnect snapshot did not preserve output.");
        Console.WriteLine("[attach-2] Ring-buffer snapshot OK");

        await second.Writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "terminate" }));
        Console.WriteLine("[terminate] Requested");
    }

    await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
    Require(host.ExitCode == 0, $"SessionHost exited with code {host.ExitCode}.");
    Console.WriteLine("PASS: ConPTY I/O, detach survival, reconnect snapshot, and terminate.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("SMOKE FAILURE: " + ex);
    try
    {
        if (!host.HasExited) host.Kill(entireProcessTree: true);
        await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }
    catch { }
    try
    {
        var stdout = await host.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(2));
        if (!string.IsNullOrWhiteSpace(stdout)) Console.Error.WriteLine("--- host stdout ---\n" + stdout);
    }
    catch { }
    try
    {
        var stderr = await host.StandardError.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(2));
        if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine("--- host stderr ---\n" + stderr);
    }
    catch { }
    return 1;
}
finally
{
    try { File.Delete(configPath); } catch { }
}

static async Task<(NamedPipeClientStream Pipe, StreamReader Reader, StreamWriter Writer)> ConnectAsync(string pipeName, TimeSpan timeout)
{
    var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    try
    {
        using var cts = new CancellationTokenSource(timeout);
        await pipe.ConnectAsync(cts.Token).WaitAsync(timeout + TimeSpan.FromSeconds(1));
        var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        return (pipe, reader, writer);
    }
    catch
    {
        pipe.Dispose();
        throw;
    }
}

static async Task<JsonElement> ReadMessageAsync(StreamReader reader, string stage, CancellationToken cancellationToken = default)
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(10));
    var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException($"[{stage}] Pipe closed.");
    using var document = JsonDocument.Parse(line);
    return document.RootElement.Clone();
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
