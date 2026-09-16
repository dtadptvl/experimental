using System.Text.Json;
using KiloSession.Shared;

namespace KiloSessionManager;

internal sealed class ProjectStore
{
    private readonly string _dataDir;
    private readonly string _filePath;

    public string RuntimeDirectory { get; }

    public ProjectStore()
    {
        _dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        RuntimeDirectory = Path.Combine(_dataDir, "runtime");
        _filePath = Path.Combine(_dataDir, "projects.json");
        Directory.CreateDirectory(RuntimeDirectory);
    }

    public List<ProjectEntry> Load()
    {
        if (!File.Exists(_filePath)) return new List<ProjectEntry>();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ProjectFile>(json, ProtocolJson.Options)?.Projects
                   ?? new List<ProjectEntry>();
        }
        catch (Exception)
        {
            var backup = _filePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            try { File.Copy(_filePath, backup, overwrite: false); } catch { }
            return new List<ProjectEntry>();
        }
    }

    public void Save(IEnumerable<ProjectEntry> projects)
    {
        Directory.CreateDirectory(_dataDir);
        var model = new ProjectFile { Projects = projects.ToList() };
        var json = JsonSerializer.Serialize(model, new JsonSerializerOptions(ProtocolJson.Options)
        {
            WriteIndented = true
        });

        var temp = _filePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _filePath, overwrite: true);
    }
}
