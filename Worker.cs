using System.Text.Json;
using FunStudio.WindowsMaintenance.Agent.Constants;
using FunStudio.WindowsMaintenance.Agent.Executors;
using FunStudio.WindowsMaintenance.Agent.Models;
using FunStudio.WindowsMaintenance.Agent.Options;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace FunStudio.WindowsMaintenance.Agent;

public class Worker : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SignalRInvokeTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<Worker> _logger;
    private readonly AgentOptions _agentOptions;
    private readonly AgentTaskExecutor _taskExecutor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ReceivedTaskLogStore _receivedTaskLogStore;
    private HubConnection? _connection;

    public Worker(
        ILogger<Worker> logger,
        IOptions<AgentOptions> agentOptions,
        AgentTaskExecutor taskExecutor,
        IHttpClientFactory httpClientFactory,
        ReceivedTaskLogStore receivedTaskLogStore)
    {
        _logger = logger;
        _agentOptions = agentOptions.Value;
        _taskExecutor = taskExecutor;
        _httpClientFactory = httpClientFactory;
        _receivedTaskLogStore = receivedTaskLogStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ValidateOptions();
        _connection = BuildConnection();
        RegisterHandlers(_connection, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_connection.State == HubConnectionState.Disconnected)
            {
                await TryStartConnectionAsync(stoppingToken);
            }

            if (_connection.State == HubConnectionState.Connected)
            {
                await SendHeartbeatAsync(stoppingToken);
            }

            try
            {
                await Task.Delay(HeartbeatInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    private HubConnection BuildConnection()
    {
        var hubUrl = BuildHubUrl();

        return new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();
    }

    private void RegisterHandlers(HubConnection connection, CancellationToken stoppingToken)
    {
        connection.On<AgentTaskMessage>("ReceiveTask", message =>
        {
            _ = Task.Run(() => HandleTaskAsync(message, "SignalR ReceiveTask", stoppingToken), stoppingToken);
        });

        connection.Reconnected += async _ =>
        {
            _logger.LogInformation("SignalR reconnected. Sending heartbeat and fetching pending tasks.");
            await SendHeartbeatAsync(stoppingToken);
            await FetchPendingTasksAsync(stoppingToken);
        };

        connection.Closed += error =>
        {
            if (error is not null)
            {
                _logger.LogWarning(error, "SignalR connection closed");
            }

            return Task.CompletedTask;
        };
    }

    private async Task TryStartConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is null || _connection.State != HubConnectionState.Disconnected)
        {
            return;
        }

        try
        {
            await _connection.StartAsync(cancellationToken);
            _logger.LogInformation("Connected to Central API SignalR hub as {MachineCode}", GetMachineCode());
            await SendHeartbeatAsync(cancellationToken);
            await FetchPendingTasksAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR connection failed. Retrying in {DelaySeconds} seconds.", ReconnectDelay.TotalSeconds);

            try
            {
                await Task.Delay(ReconnectDelay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        await SafeInvokeAsync("Heartbeat", new
        {
            MachineCode = GetMachineCode(),
            AgentVersion = GetAgentVersion(),
            SentAt = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task HandleTaskAsync(AgentTaskMessage message, string source, CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            return;
        }

        var startedAt = DateTime.UtcNow;

        try
        {
            LogReceivedTask(message, source);
            _logger.LogInformation("Starting task execution. TaskId={TaskId}, TaskType={TaskType}", message.TaskId, message.TaskType);

            await SafeInvokeAsync("TaskStarted", new
            {
                message.TaskId,
                Status = TaskStatuses.Started,
                StartedAt = startedAt
            }, cancellationToken);

            var completed = await _taskExecutor.ExecuteAsync(
                message,
                log => SendTaskLogAsync(message.TaskId, log, cancellationToken),
                cancellationToken);

            _logger.LogInformation(
                "Sending TaskCompleted. TaskId={TaskId}, TaskType={TaskType}, Status={Status}, ExitCode={ExitCode}, StdOutLength={StdOutLength}, StdErrLength={StdErrLength}",
                completed.TaskId,
                message.TaskType,
                completed.Status,
                completed.ExitCode,
                completed.StdOut?.Length ?? 0,
                completed.StdErr?.Length ?? 0);

            var sentBySignalR = await SafeInvokeAsync("TaskCompleted", completed, cancellationToken);
            if (!sentBySignalR)
            {
                await PostTaskCompletedFallbackAsync(completed, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Task handling failed before completion callback. TaskId={TaskId}, TaskType={TaskType}", message.TaskId, message.TaskType);
            var completed = new TaskCompletedDto
            {
                TaskId = message.TaskId,
                Status = TaskStatuses.Failed,
                ExitCode = 1,
                StdErr = ex.ToString(),
                StartedAt = startedAt,
                FinishedAt = DateTime.UtcNow
            };

            var sentBySignalR = await SafeInvokeAsync("TaskCompleted", completed, CancellationToken.None);
            if (!sentBySignalR)
            {
                await PostTaskCompletedFallbackAsync(completed, CancellationToken.None);
            }
        }
    }

    private async Task SendTaskLogAsync(Guid taskId, string message, CancellationToken cancellationToken)
    {
        await SafeInvokeAsync("TaskLog", new
        {
            TaskId = taskId,
            Message = message,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task<bool> SafeInvokeAsync(string methodName, object payload, CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            return false;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(SignalRInvokeTimeout);

        try
        {
            await _connection.InvokeAsync(methodName, payload, timeoutCts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning("SignalR method {MethodName} timed out after {TimeoutSeconds} seconds", methodName, SignalRInvokeTimeout.TotalSeconds);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invoke SignalR method {MethodName}", methodName);
            return false;
        }
    }

    private async Task PostTaskCompletedFallbackAsync(TaskCompletedDto completed, CancellationToken cancellationToken)
    {
        try
        {
            var baseUri = new Uri(_agentOptions.ServerUrl.TrimEnd('/') + "/");
            var uri = new Uri(baseUri, $"api/remote-agent/task-completed?machineCode={Uri.EscapeDataString(GetMachineCode())}");
            var client = _httpClientFactory.CreateClient("central-api");
            client.DefaultRequestHeaders.Remove("X-Agent-Key");
            client.DefaultRequestHeaders.Add("X-Agent-Key", GetAgentKey());

            using var response = await client.PostAsJsonAsync(uri, completed, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("TaskCompleted HTTP fallback sent. TaskId={TaskId}", completed.TaskId);
                return;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "TaskCompleted HTTP fallback returned HTTP {StatusCode}. TaskId={TaskId}, Body={Body}",
                response.StatusCode,
                completed.TaskId,
                body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TaskCompleted HTTP fallback failed. TaskId={TaskId}", completed.TaskId);
        }
    }

    private async Task FetchPendingTasksAsync(CancellationToken cancellationToken)
    {
        try
        {
            var baseUri = new Uri(_agentOptions.ServerUrl.TrimEnd('/') + "/");
            var uri = new Uri(baseUri, $"api/remote-agent/pending-tasks?machineCode={Uri.EscapeDataString(GetMachineCode())}");
            var client = _httpClientFactory.CreateClient("central-api");
            client.DefaultRequestHeaders.Remove("X-Agent-Key");
            client.DefaultRequestHeaders.Add("X-Agent-Key", GetAgentKey());

            using var response = await client.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Pending task fallback returned HTTP {StatusCode}", response.StatusCode);
                return;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var tasks = await JsonSerializer.DeserializeAsync<List<AgentTaskMessage>>(stream, JsonOptions, cancellationToken);
            foreach (var task in tasks ?? [])
            {
                await HandleTaskAsync(task, "PendingTaskFallback", cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Pending task fallback is unavailable");
        }
    }

    private string BuildHubUrl()
    {
        var baseUrl = _agentOptions.ServerUrl.TrimEnd('/');
        return $"{baseUrl}/hubs/remote-agent" +
               $"?machineCode={Uri.EscapeDataString(GetMachineCode())}" +
               $"&agentKey={Uri.EscapeDataString(GetAgentKey())}" +
               $"&agentVersion={Uri.EscapeDataString(GetAgentVersion())}";
    }

    private void LogReceivedTask(AgentTaskMessage message, string source)
    {
        _receivedTaskLogStore.Add(message, source);

        var rawJson = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        _logger.LogInformation(
            "ReceivedTask from admin. Source={Source}, TaskId={TaskId}, TaskType={TaskType}, Payload={PayloadJson}, Raw={RawJson}",
            source,
            message.TaskId,
            message.TaskType,
            message.Payload.GetRawText(),
            rawJson);
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_agentOptions.ServerUrl) ||
            string.IsNullOrWhiteSpace(_agentOptions.MachineCode) ||
            string.IsNullOrWhiteSpace(_agentOptions.AgentKey))
        {
            throw new InvalidOperationException("Agent:ServerUrl, Agent:MachineCode, and Agent:AgentKey are required.");
        }
    }

    private string GetMachineCode() => _agentOptions.MachineCode.Trim();

    private string GetAgentKey() => _agentOptions.AgentKey.Trim();

    private string GetAgentVersion() => string.IsNullOrWhiteSpace(_agentOptions.AgentVersion)
        ? "unknown"
        : _agentOptions.AgentVersion.Trim();
}
