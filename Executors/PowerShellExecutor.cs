using System.Diagnostics;
using System.Text;
using FunStudio.WindowsMaintenance.Agent.Constants;
using FunStudio.WindowsMaintenance.Agent.Models;
using FunStudio.WindowsMaintenance.Agent.Options;
using Microsoft.Extensions.Options;

namespace FunStudio.WindowsMaintenance.Agent.Executors;

public sealed class PowerShellExecutor
{
    private readonly PowerShellOptions _options;
    private readonly ILogger<PowerShellExecutor> _logger;

    public PowerShellExecutor(IOptions<PowerShellOptions> options, ILogger<PowerShellExecutor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<TaskExecutionResult> RunAdminAsync(PowerShellPayload payload, CancellationToken cancellationToken)
    {
        if (!_options.AllowAdminPowerShell)
        {
            return Task.FromResult(TaskExecutionResult.Failure("Admin PowerShell is disabled by configuration."));
        }

        if (string.IsNullOrWhiteSpace(payload.Script))
        {
            return Task.FromResult(TaskExecutionResult.Failure("PowerShell script is required."));
        }

        return RunAsync(
            $"-NoProfile -ExecutionPolicy Bypass -Command {Quote(payload.Script)}",
            payload.TimeoutSeconds,
            payload.WorkingDirectory,
            payload.EnvironmentVariables,
            cancellationToken);
    }

    public Task<TaskExecutionResult> RunUserAsync(PowerShellPayload payload, CancellationToken cancellationToken)
    {
        if (!_options.AllowUserPowerShell)
        {
            return Task.FromResult(TaskExecutionResult.Failure("User PowerShell is disabled by configuration."));
        }

        if (string.IsNullOrWhiteSpace(payload.Script))
        {
            return Task.FromResult(TaskExecutionResult.Failure("PowerShell script is required."));
        }

        return RunAsync(
            $"-NoProfile -ExecutionPolicy Bypass -Command {Quote(payload.Script)}",
            payload.TimeoutSeconds,
            payload.WorkingDirectory,
            payload.EnvironmentVariables,
            cancellationToken);
    }

    public Task<TaskExecutionResult> RunFileAdminAsync(PowerShellFilePayload payload, CancellationToken cancellationToken)
    {
        if (!_options.AllowAdminPowerShell)
        {
            return Task.FromResult(TaskExecutionResult.Failure("Admin PowerShell is disabled by configuration."));
        }

        return RunFileAsync(payload, cancellationToken);
    }

    public Task<TaskExecutionResult> RunFileUserAsync(PowerShellFilePayload payload, CancellationToken cancellationToken)
    {
        if (!_options.AllowUserPowerShell)
        {
            return Task.FromResult(TaskExecutionResult.Failure("User PowerShell is disabled by configuration."));
        }

        return RunFileAsync(payload, cancellationToken);
    }

    private Task<TaskExecutionResult> RunFileAsync(PowerShellFilePayload payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.ScriptPath))
        {
            return Task.FromResult(TaskExecutionResult.Failure("PowerShell scriptPath is required."));
        }

        if (!File.Exists(payload.ScriptPath))
        {
            return Task.FromResult(TaskExecutionResult.Failure($"PowerShell file was not found: {payload.ScriptPath}"));
        }

        var arguments = string.IsNullOrWhiteSpace(payload.Arguments) ? "" : " " + payload.Arguments;
        return RunAsync(
            $"-NoProfile -ExecutionPolicy Bypass -File {Quote(payload.ScriptPath)}{arguments}",
            payload.TimeoutSeconds,
            payload.WorkingDirectory,
            payload.EnvironmentVariables,
            cancellationToken);
    }

    public Task<TaskExecutionResult> RunAsync(string script, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return Task.FromResult(TaskExecutionResult.Failure("PowerShell script is required."));
        }

        return RunAsync(
            $"-NoProfile -ExecutionPolicy Bypass -Command {Quote(script)}",
            timeoutSeconds,
            workingDirectory: null,
            environmentVariables: null,
            cancellationToken);
    }

    public async Task<TaskExecutionResult> RunAsync(
        string arguments,
        int timeoutSeconds,
        string? workingDirectory,
        Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return TaskExecutionResult.Failure("PowerShell arguments are required.");
        }

        timeoutSeconds = NormalizeTimeout(timeoutSeconds);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        try
        {
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                process.StartInfo.WorkingDirectory = workingDirectory;
            }

            if (environmentVariables is not null)
            {
                foreach (var item in environmentVariables)
                {
                    process.StartInfo.Environment[item.Key] = item.Value;
                }
            }

            process.OutputDataReceived += (_, args) => AppendLine(stdout, args.Data);
            process.ErrorDataReceived += (_, args) => AppendLine(stderr, args.Data);

            _logger.LogInformation("Starting PowerShell task with timeout {TimeoutSeconds}s", timeoutSeconds);

            if (!process.Start())
            {
                return TaskExecutionResult.Failure("Failed to start powershell.exe.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(timeoutCts.Token);
            await Task.Delay(100, CancellationToken.None);

            return new TaskExecutionResult
            {
                Status = process.ExitCode == 0 ? TaskStatuses.Succeeded : TaskStatuses.Failed,
                ExitCode = process.ExitCode,
                StdOut = Truncate(stdout.ToString()),
                StdErr = Truncate(stderr.ToString())
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            return new TaskExecutionResult
            {
                Status = TaskStatuses.TimedOut,
                ExitCode = -1,
                StdOut = Truncate(stdout.ToString()),
                StdErr = $"PowerShell task timed out after {timeoutSeconds} seconds."
            };
        }
        catch (Exception ex)
        {
            KillProcessTree(process);
            return TaskExecutionResult.Failure(ex.Message);
        }
    }

    private int NormalizeTimeout(int timeoutSeconds)
    {
        if (timeoutSeconds <= 0)
        {
            return _options.MaxTimeoutSeconds;
        }

        return Math.Min(timeoutSeconds, _options.MaxTimeoutSeconds);
    }

    private void AppendLine(StringBuilder builder, string? line)
    {
        if (line is null || builder.Length >= _options.MaxOutputLength)
        {
            return;
        }

        builder.AppendLine(line);
    }

    private string Truncate(string value)
    {
        return value.Length <= _options.MaxOutputLength
            ? value
            : value[.._options.MaxOutputLength] + Environment.NewLine + "[output truncated]";
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill PowerShell process tree");
        }
    }
}
