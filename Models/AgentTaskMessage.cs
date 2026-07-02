using System.Text.Json;

namespace FunStudio.WindowsMaintenance.Agent.Models;

public sealed class AgentTaskMessage
{
    public Guid TaskId { get; set; }
    public string TaskType { get; set; } = "";
    public JsonElement Payload { get; set; }
}
