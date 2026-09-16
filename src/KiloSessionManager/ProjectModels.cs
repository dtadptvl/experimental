namespace KiloSessionManager;

internal sealed class ProjectEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool HasStarted { get; set; }
    public bool ManualStop { get; set; }
    public RuntimeRef? Runtime { get; set; }

    public static ProjectEntry Create(string path, string? requestedName)
    {
        path = System.IO.Path.GetFullPath(path);
        var fallback = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
        if (string.IsNullOrWhiteSpace(fallback)) fallback = path;

        return new ProjectEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(requestedName) ? fallback : requestedName.Trim(),
            Path = path
        };
    }
}

internal sealed class RuntimeRef
{
    public string InstanceId { get; set; } = "";
    public string PipeName { get; set; } = "";
    public int HostPid { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public string ConfigPath { get; set; } = "";
}

internal sealed class ProjectFile
{
    public int Version { get; set; } = 1;
    public List<ProjectEntry> Projects { get; set; } = new();
}
