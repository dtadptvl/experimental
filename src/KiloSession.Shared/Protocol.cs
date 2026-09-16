using System.Text.Json;
using System.Text.Json.Serialization;

namespace KiloSession.Shared;

public sealed class HostConfig
{
    public string ProjectId { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string PipeName { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string Executable { get; set; } = "cmd.exe";
    public string Arguments { get; set; } = "/d /q /c kilo --auto";
    public int RingBufferBytes { get; set; } = 8 * 1024 * 1024;
}

public sealed class PipeMessage
{
    public string Type { get; set; } = "";
    public string? ProjectId { get; set; }
    public string? InstanceId { get; set; }
    public string? Data { get; set; }
    public string? Message { get; set; }
    public int? HostPid { get; set; }
    public int? ChildPid { get; set; }
    public int? Cols { get; set; }
    public int? Rows { get; set; }
    public bool? Running { get; set; }
}

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Serialize(PipeMessage value) => JsonSerializer.Serialize(value, Options);
    public static PipeMessage? Deserialize(string json) => JsonSerializer.Deserialize<PipeMessage>(json, Options);
}
