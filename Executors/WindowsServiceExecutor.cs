using System.ServiceProcess;
using FunStudio.WindowsMaintenance.Agent.Constants;
using FunStudio.WindowsMaintenance.Agent.Models;

namespace FunStudio.WindowsMaintenance.Agent.Executors;

public sealed class WindowsServiceExecutor
{
    private readonly HashSet<string> _managedServices;

    public WindowsServiceExecutor(IConfiguration configuration)
    {
        _managedServices = configuration.GetSection("ManagedServices").Get<string[]>()?
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
    }

    public Task<TaskExecutionResult> GetStatusAsync(ServiceTaskPayload payload, CancellationToken cancellationToken)
    {
        return Task.FromResult(Execute(payload.ServiceName, _ => { }, _ => false));
    }

    public Task<TaskExecutionResult> StartAsync(ServiceTaskPayload payload, CancellationToken cancellationToken)
    {
        return Task.FromResult(Execute(payload.ServiceName, service =>
        {
            if (service.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            {
                return;
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(NormalizeTimeout(payload.TimeoutSeconds)));
        }, service => service.Status == ServiceControllerStatus.Running));
    }

    public Task<TaskExecutionResult> StopAsync(ServiceTaskPayload payload, CancellationToken cancellationToken)
    {
        return Task.FromResult(Execute(payload.ServiceName, service =>
        {
            if (service.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
            {
                return;
            }

            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(NormalizeTimeout(payload.TimeoutSeconds)));
        }, service => service.Status == ServiceControllerStatus.Stopped));
    }

    public Task<TaskExecutionResult> RestartAsync(ServiceTaskPayload payload, CancellationToken cancellationToken)
    {
        return Task.FromResult(Execute(payload.ServiceName, service =>
        {
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(NormalizeTimeout(payload.TimeoutSeconds)));
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(NormalizeTimeout(payload.TimeoutSeconds)));
        }, service => service.Status == ServiceControllerStatus.Running));
    }

    private TaskExecutionResult Execute(string serviceName, Action<ServiceController> action, Func<ServiceController, bool> successCheck)
    {
        if (!_managedServices.Contains(serviceName))
        {
            return TaskExecutionResult.Failure($"Service '{serviceName}' is not in ManagedServices whitelist.");
        }

        try
        {
            using var service = new ServiceController(serviceName);
            var before = service.Status;
            action(service);
            service.Refresh();

            var status = service.Status;
            var ok = successCheck(service) || before == status;

            return new TaskExecutionResult
            {
                Status = ok ? TaskStatuses.Succeeded : TaskStatuses.Failed,
                ExitCode = ok ? 0 : 1,
                StdOut = $"Service '{serviceName}' status: {status}"
            };
        }
        catch (Exception ex)
        {
            return TaskExecutionResult.Failure(ex.Message);
        }
    }

    private static int NormalizeTimeout(int timeoutSeconds) => timeoutSeconds <= 0 ? 60 : timeoutSeconds;
}
