using System.Text.Json;

namespace FunStudio.WindowsMaintenance.Agent.Models;

public sealed class ReceivedTaskLogEntry
{
    public DateTime ReceivedAt { get; set; }
    public string Source { get; set; } = "";
    public Guid TaskId { get; set; }
    public string TaskType { get; set; } = "";
    public JsonElement Payload { get; set; }
    public string RawJson { get; set; } = "";
}
