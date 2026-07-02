namespace FunStudio.WindowsMaintenance.Agent.Models;

public sealed class TaskExecutionResult
{
    public string Status { get; set; } = "";
    public int ExitCode { get; set; }
    public string StdOut { get; set; } = "";
    public string StdErr { get; set; } = "";

    public static TaskExecutionResult Success(string stdout, int exitCode = 0) => new()
    {
        Status = Constants.TaskStatuses.Succeeded,
        ExitCode = exitCode,
        StdOut = stdout
    };

    public static TaskExecutionResult Failure(string stderr, int exitCode = 1) => new()
    {
        Status = Constants.TaskStatuses.Failed,
        ExitCode = exitCode,
        StdErr = stderr
    };
}
