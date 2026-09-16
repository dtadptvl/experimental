using System.Diagnostics;
using System.Text.Json;
using KiloSession.Shared;

namespace KiloSessionManager;

internal sealed class SessionHostLauncher
{
    private readonly ProjectStore _store;

    public SessionHostLauncher(ProjectStore store) => _store = store;

    public RuntimeRef Launch(ProjectEntry project, bool resume)
    {
        if (!Directory.Exists(project.Path))
            throw new DirectoryNotFoundException(project.Path);

        var hostExe = Path.Combine(AppContext.BaseDirectory, "KiloSessionHost.exe");
        if (!File.Exists(hostExe))
            throw new FileNotFoundException("KiloSessionHost.exe was not found beside KiloSessionManager.exe.", hostExe);

        var instanceId = Guid.NewGuid().ToString("N");
        var pipeName = $"kilo-manager-{project.Id}-{instanceId}";
        var configPath = Path.Combine(_store.RuntimeDirectory, $"{project.Id}-{instanceId}.json");
        var comspec = Environment.GetEnvironmentVariable("COMSPEC");
        if (string.IsNullOrWhiteSpace(comspec)) comspec = "cmd.exe";

        var kiloArgs = resume ? "kilo --auto --continue" : "kilo --auto";
        var config = new HostConfig
        {
            ProjectId = project.Id,
            InstanceId = instanceId,
            PipeName = pipeName,
            WorkingDirectory = project.Path,
            Executable = comspec,
            Arguments = $"/d /q /c {kiloArgs}",
            RingBufferBytes = 8 * 1024 * 1024
        };

        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions(ProtocolJson.Options)
        {
            WriteIndented = true
        }));

        var pid = DetachedProcess.Start(
            hostExe,
            $"--config \"{configPath}\"",
            AppContext.BaseDirectory);

        DateTime startedAt;
        try
        {
            using var process = Process.GetProcessById(pid);
            startedAt = process.StartTime.ToUniversalTime();
        }
        catch
        {
            startedAt = DateTime.UtcNow;
        }

        return new RuntimeRef
        {
            InstanceId = instanceId,
            PipeName = pipeName,
            HostPid = pid,
            StartedAtUtc = startedAt,
            ConfigPath = configPath
        };
    }

    public static bool IsAlive(RuntimeRef? runtime)
    {
        if (runtime is null || runtime.HostPid <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(runtime.HostPid);
            if (process.HasExited) return false;
            var start = process.StartTime.ToUniversalTime();
            return Math.Abs((start - runtime.StartedAtUtc).TotalSeconds) < 5;
        }
        catch
        {
            return false;
        }
    }
}
