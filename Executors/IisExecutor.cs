using FunStudio.WindowsMaintenance.Agent.Models;

namespace FunStudio.WindowsMaintenance.Agent.Executors;

public sealed class IisExecutor
{
    private readonly PowerShellExecutor _powerShellExecutor;

    public IisExecutor(PowerShellExecutor powerShellExecutor)
    {
        _powerShellExecutor = powerShellExecutor;
    }

    public Task<TaskExecutionResult> StartAsync(IisTaskPayload payload, CancellationToken cancellationToken)
    {
        return RunIisResetAsync("/start", payload.TimeoutSeconds, cancellationToken);
    }

    public Task<TaskExecutionResult> StopAsync(IisTaskPayload payload, CancellationToken cancellationToken)
    {
        return RunIisResetAsync("/stop", payload.TimeoutSeconds, cancellationToken);
    }

    public Task<TaskExecutionResult> RestartAsync(IisTaskPayload payload, CancellationToken cancellationToken)
    {
        return RunIisResetAsync("/restart", payload.TimeoutSeconds, cancellationToken);
    }

    private Task<TaskExecutionResult> RunIisResetAsync(string argument, int timeoutSeconds, CancellationToken cancellationToken)
    {
        return _powerShellExecutor.RunAsync($"iisreset {argument}", timeoutSeconds, cancellationToken);
    }
}
