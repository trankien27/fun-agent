using System.Text.Json;
using FunStudio.WindowsMaintenance.Agent.Constants;
using FunStudio.WindowsMaintenance.Agent.Models;
using FunStudio.WindowsMaintenance.Agent.Options;
using FunStudio.WindowsMaintenance.Agent.Services;
using Microsoft.Extensions.Options;

namespace FunStudio.WindowsMaintenance.Agent.Executors;

public sealed class AgentTaskExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WindowsServiceExecutor _windowsServiceExecutor;
    private readonly ManagedFolderExecutor _managedFolderExecutor;
    private readonly IisExecutor _iisExecutor;
    private readonly PowerShellExecutor _powerShellExecutor;
    private readonly TransactionReader _transactionReader;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PrintImageOptions _printImageOptions;
    private readonly ILogger<AgentTaskExecutor> _logger;

    public AgentTaskExecutor(
        WindowsServiceExecutor windowsServiceExecutor,
        ManagedFolderExecutor managedFolderExecutor,
        IisExecutor iisExecutor,
        PowerShellExecutor powerShellExecutor,
        TransactionReader transactionReader,
        IHttpClientFactory httpClientFactory,
        IOptions<PrintImageOptions> printImageOptions,
        ILogger<AgentTaskExecutor> logger)
    {
        _windowsServiceExecutor = windowsServiceExecutor;
        _managedFolderExecutor = managedFolderExecutor;
        _iisExecutor = iisExecutor;
        _powerShellExecutor = powerShellExecutor;
        _transactionReader = transactionReader;
        _httpClientFactory = httpClientFactory;
        _printImageOptions = printImageOptions.Value;
        _logger = logger;
    }

    public async Task<TaskCompletedDto> ExecuteAsync(
        AgentTaskMessage message,
        Func<string, Task> writeLogAsync,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        TaskExecutionResult result;

        try
        {
            await writeLogAsync($"Executing task {message.TaskType}");
            result = await DispatchAsync(message, writeLogAsync, cancellationToken);
            await writeLogAsync($"Task {message.TaskType} finished with status {result.Status}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while executing task {TaskId}", message.TaskId);
            result = TaskExecutionResult.Failure(ex.Message);
            await writeLogAsync($"Task failed: {ex.Message}");
        }

        return new TaskCompletedDto
        {
            TaskId = message.TaskId,
            Status = result.Status,
            ExitCode = result.ExitCode,
            StdOut = result.StdOut,
            StdErr = result.StdErr,
            StartedAt = startedAt,
            FinishedAt = DateTime.UtcNow
        };
    }

    private Task<TaskExecutionResult> DispatchAsync(
        AgentTaskMessage message,
        Func<string, Task> writeLogAsync,
        CancellationToken cancellationToken)
    {
        return message.TaskType switch
        {
            TaskTypes.GetServiceStatus => _windowsServiceExecutor.GetStatusAsync(Read<ServiceTaskPayload>(message), cancellationToken),
            TaskTypes.StartService => _windowsServiceExecutor.StartAsync(Read<ServiceTaskPayload>(message), cancellationToken),
            TaskTypes.StopService => _windowsServiceExecutor.StopAsync(Read<ServiceTaskPayload>(message), cancellationToken),
            TaskTypes.RestartService => _windowsServiceExecutor.RestartAsync(Read<ServiceTaskPayload>(message), cancellationToken),

            TaskTypes.GetFolderInfo => _managedFolderExecutor.GetFolderInfoAsync(Read<FolderInfoPayload>(message), cancellationToken),
            TaskTypes.ListFolderFiles => _managedFolderExecutor.ListFolderFilesAsync(Read<ListFolderFilesPayload>(message), cancellationToken),
            TaskTypes.DownloadToFolder => _managedFolderExecutor.DownloadToFolderAsync(Read<DownloadToFolderPayload>(message), cancellationToken),
            TaskTypes.ExtractZipToFolder => _managedFolderExecutor.ExtractZipToFolderAsync(Read<ExtractZipToFolderPayload>(message), cancellationToken),
            TaskTypes.DeleteFolderFile => _managedFolderExecutor.DeleteFolderFileAsync(Read<DeleteFolderFilePayload>(message), cancellationToken),
            TaskTypes.CleanFolder => _managedFolderExecutor.CleanFolderAsync(Read<CleanFolderPayload>(message), cancellationToken),

            TaskTypes.StartIis => _iisExecutor.StartAsync(ReadOrDefault<IisTaskPayload>(message), cancellationToken),
            TaskTypes.StopIis => _iisExecutor.StopAsync(ReadOrDefault<IisTaskPayload>(message), cancellationToken),
            TaskTypes.RestartIis => _iisExecutor.RestartAsync(ReadOrDefault<IisTaskPayload>(message), cancellationToken),

            TaskTypes.RunPowerShellAdmin => _powerShellExecutor.RunAdminAsync(Read<PowerShellPayload>(message), cancellationToken),
            TaskTypes.RunPowerShellUser => _powerShellExecutor.RunUserAsync(Read<PowerShellPayload>(message), cancellationToken),
            TaskTypes.RunPowerShellFileAdmin => _powerShellExecutor.RunFileAdminAsync(Read<PowerShellFilePayload>(message), cancellationToken),
            TaskTypes.RunPowerShellFileUser => _powerShellExecutor.RunFileUserAsync(Read<PowerShellFilePayload>(message), cancellationToken),

            TaskTypes.GetUltraViewerPreferId => GetUltraViewerPreferIdAsync(cancellationToken),

            TaskTypes.GetTransactions => GetTransactionsAsync(cancellationToken),
            TaskTypes.PrintImage => PrintImageAsync(Read<PrintImageRequest>(message), cancellationToken),

            TaskTypes.UpdateVersion => UpdateVersionAsync(Read<DeploymentTaskPayload>(message), writeLogAsync, cancellationToken),
            TaskTypes.DeployFsAsyncTransaction => DeployServicePackageAsync(
                Read<DeploymentTaskPayload>(message),
                "FSAsyncTransaction",
                "FS_ASYNC_TRANSACTION_INSTALLER",
                writeLogAsync,
                cancellationToken),
            TaskTypes.DeployFsUpdateSync => DeployServicePackageAsync(
                Read<DeploymentTaskPayload>(message),
                "FSUpdateSync",
                "FS_UPDATE_SYNC_INSTALLER",
                writeLogAsync,
                cancellationToken),
            TaskTypes.DeployAppForm => DeployFolderPackageAsync(
                Read<DeploymentTaskPayload>(message),
                "FS_UPDATE_APP_FORM",
                writeLogAsync,
                cancellationToken),

            _ => Task.FromResult(TaskExecutionResult.Failure($"Unsupported task type '{message.TaskType}'."))
        };
    }

    private async Task<TaskExecutionResult> GetUltraViewerPreferIdAsync(CancellationToken cancellationToken)
    {
        const string script = """
            $items = Get-ChildItem "HKCU:\Software", "HKLM:\Software", "HKLM:\Software\WOW6432Node" -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match "UltraViewer" } |
                ForEach-Object { Get-ItemProperty $_.PsPath }

            $preferId = $null
            foreach ($item in $items) {
                $property = $item.PSObject.Properties |
                    Where-Object { $_.Name -ieq "preferId" -or $_.Name -ieq "PreferredId" -or $_.Name -ieq "ID" -or $_.Name -ieq "ClientID" } |
                    Select-Object -First 1

                if ($property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                    $preferId = [string]$property.Value
                    break
                }
            }

            [PSCustomObject]@{
                preferId = $preferId
                found = $null -ne $preferId
                items = @($items | Select-Object PSPath, PSChildName, preferId, PreferredId, ID, ClientID)
            } | ConvertTo-Json -Depth 5 -Compress
            """;

        return await _powerShellExecutor.RunAsync(script, timeoutSeconds: 30, cancellationToken);
    }

    private async Task<TaskExecutionResult> GetTransactionsAsync(CancellationToken cancellationToken)
    {
        var transactions = await _transactionReader.GetTransactionsAsync(cancellationToken);
        return TaskExecutionResult.Success(JsonSerializer.Serialize(transactions, JsonOptions));
    }

    private async Task<TaskExecutionResult> PrintImageAsync(PrintImageRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TransactionId))
        {
            return TaskExecutionResult.Failure("transactionId is required.");
        }

        var client = _httpClientFactory.CreateClient("print-image");
        using var response = await client.PostAsJsonAsync(_printImageOptions.Url, request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var output = JsonSerializer.Serialize(new
        {
            StatusCode = (int)response.StatusCode,
            response.IsSuccessStatusCode,
            Body = body
        }, JsonOptions);

        return response.IsSuccessStatusCode
            ? TaskExecutionResult.Success(output)
            : new TaskExecutionResult
            {
                Status = TaskStatuses.Failed,
                ExitCode = (int)response.StatusCode,
                StdOut = output,
                StdErr = body
            };
    }

    private async Task<TaskExecutionResult> UpdateVersionAsync(
        DeploymentTaskPayload payload,
        Func<string, Task> writeLogAsync,
        CancellationToken cancellationToken)
    {
        var output = new List<string>();
        var iisStopped = false;

        try
        {
            await StepAsync("Stopping IIS", output, writeLogAsync);
            var stopResult = await _iisExecutor.StopAsync(new IisTaskPayload
            {
                TimeoutSeconds = payload.TimeoutSeconds
            }, cancellationToken);
            AddStepResult("STOP_IIS", stopResult, output);
            if (!IsSuccess(stopResult))
            {
                return FailureFromSteps("Failed to stop IIS.", output, stopResult);
            }

            iisStopped = true;

            await StepAsync("Extracting BE package to BE_PHOTOBOOTH_DEPLOY", output, writeLogAsync);
            var extractResult = await ExtractToFolderAsync(payload, "BE_PHOTOBOOTH_DEPLOY", cancellationToken);
            AddStepResult("EXTRACT_ZIP_TO_FOLDER", extractResult, output);
            if (!IsSuccess(extractResult))
            {
                return FailureFromSteps("Failed to extract BE package.", output, extractResult);
            }

            await StepAsync("Starting IIS", output, writeLogAsync);
            var startResult = await _iisExecutor.StartAsync(new IisTaskPayload
            {
                TimeoutSeconds = payload.TimeoutSeconds
            }, cancellationToken);
            AddStepResult("START_IIS", startResult, output);
            iisStopped = false;
            if (!IsSuccess(startResult))
            {
                return FailureFromSteps("Failed to start IIS.", output, startResult);
            }

            await StepAsync("Running iisreset /restart as service account", output, writeLogAsync);
            var restartResult = await _iisExecutor.RestartAsync(new IisTaskPayload
            {
                TimeoutSeconds = payload.TimeoutSeconds
            }, cancellationToken);
            AddStepResult("RESTART_IIS", restartResult, output);
            if (!IsSuccess(restartResult))
            {
                return FailureFromSteps("Failed to restart IIS.", output, restartResult);
            }

            return TaskExecutionResult.Success(string.Join(Environment.NewLine, output));
        }
        finally
        {
            if (iisStopped)
            {
                await StepAsync("Trying to start IIS after failure", output, writeLogAsync);
                var startResult = await _iisExecutor.StartAsync(new IisTaskPayload
                {
                    TimeoutSeconds = payload.TimeoutSeconds
                }, cancellationToken);
                AddStepResult("START_IIS_FINALLY", startResult, output);
            }
        }
    }

    private async Task<TaskExecutionResult> DeployServicePackageAsync(
        DeploymentTaskPayload payload,
        string serviceName,
        string folderKey,
        Func<string, Task> writeLogAsync,
        CancellationToken cancellationToken)
    {
        var output = new List<string>();
        var serviceStopped = false;

        try
        {
            await StepAsync($"Stopping service {serviceName}", output, writeLogAsync);
            var stopResult = await _windowsServiceExecutor.StopAsync(new ServiceTaskPayload
            {
                ServiceName = serviceName,
                TimeoutSeconds = payload.TimeoutSeconds
            }, cancellationToken);
            AddStepResult("STOP_SERVICE", stopResult, output);
            if (!IsSuccess(stopResult))
            {
                return FailureFromSteps($"Failed to stop service {serviceName}.", output, stopResult);
            }

            serviceStopped = true;

            await StepAsync($"Extracting package to {folderKey}", output, writeLogAsync);
            var extractResult = await ExtractToFolderAsync(payload, folderKey, cancellationToken);
            AddStepResult("EXTRACT_ZIP_TO_FOLDER", extractResult, output);
            if (!IsSuccess(extractResult))
            {
                return FailureFromSteps($"Failed to extract package to {folderKey}.", output, extractResult);
            }

            await StepAsync($"Starting service {serviceName}", output, writeLogAsync);
            var startResult = await _windowsServiceExecutor.StartAsync(new ServiceTaskPayload
            {
                ServiceName = serviceName,
                TimeoutSeconds = payload.TimeoutSeconds
            }, cancellationToken);
            AddStepResult("START_SERVICE", startResult, output);
            serviceStopped = false;
            if (!IsSuccess(startResult))
            {
                return FailureFromSteps($"Failed to start service {serviceName}.", output, startResult);
            }

            return TaskExecutionResult.Success(string.Join(Environment.NewLine, output));
        }
        finally
        {
            if (serviceStopped)
            {
                await StepAsync($"Trying to start service {serviceName} after failure", output, writeLogAsync);
                var startResult = await _windowsServiceExecutor.StartAsync(new ServiceTaskPayload
                {
                    ServiceName = serviceName,
                    TimeoutSeconds = payload.TimeoutSeconds
                }, cancellationToken);
                AddStepResult("START_SERVICE_FINALLY", startResult, output);
            }
        }
    }

    private async Task<TaskExecutionResult> DeployFolderPackageAsync(
        DeploymentTaskPayload payload,
        string folderKey,
        Func<string, Task> writeLogAsync,
        CancellationToken cancellationToken)
    {
        var output = new List<string>();
        await StepAsync($"Extracting package to {folderKey}", output, writeLogAsync);
        var extractResult = await ExtractToFolderAsync(payload, folderKey, cancellationToken);
        AddStepResult("EXTRACT_ZIP_TO_FOLDER", extractResult, output);

        return IsSuccess(extractResult)
            ? TaskExecutionResult.Success(string.Join(Environment.NewLine, output))
            : FailureFromSteps($"Failed to extract package to {folderKey}.", output, extractResult);
    }

    private Task<TaskExecutionResult> ExtractToFolderAsync(
        DeploymentTaskPayload payload,
        string folderKey,
        CancellationToken cancellationToken)
    {
        return _managedFolderExecutor.ExtractZipToFolderAsync(new ExtractZipToFolderPayload
        {
            FolderKey = folderKey,
            FileUrl = payload.FileUrl,
            Overwrite = true,
            CleanTargetBeforeExtract = payload.CleanTargetBeforeExtract,
            TimeoutSeconds = payload.TimeoutSeconds
        }, cancellationToken);
    }

    private static async Task StepAsync(string message, List<string> output, Func<string, Task> writeLogAsync)
    {
        output.Add(message);
        await writeLogAsync(message);
    }

    private static void AddStepResult(string step, TaskExecutionResult result, List<string> output)
    {
        output.Add($"[{step}] Status={result.Status}; ExitCode={result.ExitCode}");

        if (!string.IsNullOrWhiteSpace(result.StdOut))
        {
            output.Add(result.StdOut.Trim());
        }

        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            output.Add(result.StdErr.Trim());
        }
    }

    private static bool IsSuccess(TaskExecutionResult result)
    {
        return string.Equals(result.Status, TaskStatuses.Succeeded, StringComparison.OrdinalIgnoreCase)
            && result.ExitCode == 0;
    }

    private static TaskExecutionResult FailureFromSteps(string message, List<string> output, TaskExecutionResult result)
    {
        return new TaskExecutionResult
        {
            Status = result.Status,
            ExitCode = result.ExitCode == 0 ? 1 : result.ExitCode,
            StdOut = string.Join(Environment.NewLine, output),
            StdErr = string.IsNullOrWhiteSpace(result.StdErr) ? message : result.StdErr
        };
    }

    private static T Read<T>(AgentTaskMessage message)
    {
        var payload = message.Payload.Deserialize<T>(JsonOptions);
        return payload ?? throw new InvalidOperationException($"Invalid payload for task type '{message.TaskType}'.");
    }

    private static T ReadOrDefault<T>(AgentTaskMessage message)
        where T : new()
    {
        if (message.Payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new T();
        }

        return Read<T>(message);
    }
}
