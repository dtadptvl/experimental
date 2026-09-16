using System.Text.Json;
using KiloSession.Shared;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("KiloSessionHost requires Windows 10 1809 or newer (ConPTY). ");
    return 2;
}

if (args.Length != 2 || !string.Equals(args[0], "--config", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: KiloSessionHost --config <path>");
    return 2;
}

try
{
    var json = await File.ReadAllTextAsync(args[1]);
    var config = JsonSerializer.Deserialize<HostConfig>(json, ProtocolJson.Options)
        ?? throw new InvalidOperationException("Invalid host config.");

    if (string.IsNullOrWhiteSpace(config.ProjectId) ||
        string.IsNullOrWhiteSpace(config.InstanceId) ||
        string.IsNullOrWhiteSpace(config.PipeName) ||
        string.IsNullOrWhiteSpace(config.WorkingDirectory))
    {
        throw new InvalidOperationException("Host config is missing required fields.");
    }

    using var host = new SessionHost(config);
    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
