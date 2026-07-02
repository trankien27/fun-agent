using System.Collections.Concurrent;
using System.Text.Json;
using FunStudio.WindowsMaintenance.Agent.Models;

namespace FunStudio.WindowsMaintenance.Agent;

public sealed class ReceivedTaskLogStore
{
    private const int MaxItems = 100;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ConcurrentQueue<ReceivedTaskLogEntry> _entries = new();

    public void Add(AgentTaskMessage message, string source)
    {
        _entries.Enqueue(new ReceivedTaskLogEntry
        {
            ReceivedAt = DateTime.UtcNow,
            Source = source,
            TaskId = message.TaskId,
            TaskType = message.TaskType,
            Payload = message.Payload.Clone(),
            RawJson = JsonSerializer.Serialize(message, JsonOptions)
        });

        while (_entries.Count > MaxItems && _entries.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyCollection<ReceivedTaskLogEntry> GetRecent()
    {
        return _entries
            .OrderByDescending(entry => entry.ReceivedAt)
            .ToArray();
    }
}
