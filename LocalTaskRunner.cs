using System.Text.Json;
using FunStudio.WindowsMaintenance.Agent.Executors;
using FunStudio.WindowsMaintenance.Agent.Models;

namespace FunStudio.WindowsMaintenance.Agent;

public static class LocalTaskRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static async Task<int> RunAsync(IHost host, string taskFile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskFile))
        {
            Console.Error.WriteLine("Missing task file. Usage: --run-task <task.json>");
            return 2;
        }

        if (!File.Exists(taskFile))
        {
            Console.Error.WriteLine($"Task file not found: {taskFile}");
            return 2;
        }

        var json = await File.ReadAllTextAsync(taskFile, cancellationToken);
        var message = JsonSerializer.Deserialize<AgentTaskMessage>(json, JsonOptions);
        if (message is null)
        {
            Console.Error.WriteLine("Invalid task JSON.");
            return 2;
        }

        if (message.TaskId == Guid.Empty)
        {
            message.TaskId = Guid.NewGuid();
        }

        var executor = host.Services.GetRequiredService<AgentTaskExecutor>();
        var result = await executor.ExecuteAsync(
            message,
            log =>
            {
                Console.WriteLine($"[TaskLog] {log}");
                return Task.CompletedTask;
            },
            cancellationToken);

        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return result.ExitCode == 0 ? 0 : 1;
    }
}
