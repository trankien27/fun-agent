namespace FunStudio.WindowsMaintenance.Agent.Models;

public sealed class TaskCompletedDto
{
    public Guid TaskId { get; set; }
    public string Status { get; set; } = "";
    public int ExitCode { get; set; }
    public string StdOut { get; set; } = "";
    public string StdErr { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
}
