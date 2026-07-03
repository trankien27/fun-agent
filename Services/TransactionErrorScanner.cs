using System.Text.Json;
using FunStudio.WindowsMaintenance.Agent.Models;
using FunStudio.WindowsMaintenance.Agent.Options;
using Microsoft.Extensions.Options;

namespace FunStudio.WindowsMaintenance.Agent.Services;

public sealed class TransactionErrorScanner : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<TransactionErrorScanner> _logger;
    private readonly TransactionReader _transactionReader;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentOptions _agentOptions;
    private readonly TransactionErrorScannerOptions _scannerOptions;

    public TransactionErrorScanner(
        ILogger<TransactionErrorScanner> logger,
        TransactionReader transactionReader,
        IHttpClientFactory httpClientFactory,
        IOptions<AgentOptions> agentOptions,
        IOptions<TransactionErrorScannerOptions> scannerOptions)
    {
        _logger = logger;
        _transactionReader = transactionReader;
        _httpClientFactory = httpClientFactory;
        _agentOptions = agentOptions.Value;
        _scannerOptions = scannerOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_scannerOptions.Enabled)
        {
            _logger.LogInformation("Transaction error scanner is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await ScanAndPushAsync(stoppingToken);

            var interval = TimeSpan.FromSeconds(Math.Max(30, _scannerOptions.IntervalSeconds));
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task ScanAndPushAsync(CancellationToken cancellationToken)
    {
        try
        {
            var transactions = await _transactionReader.GetTodayErrorTransactionsAsync(
                _scannerOptions.SuccessStatus,
                _scannerOptions.Limit,
                cancellationToken);

            var items = transactions
                .Select(ToPushItem)
                .Where(item => !string.IsNullOrWhiteSpace(item.TransactionId))
                .ToList();

            if (items.Count == 0)
            {
                _logger.LogDebug("Transaction error scanner found no today error transactions.");
                return;
            }

            var baseUri = new Uri(_agentOptions.ServerUrl.TrimEnd('/') + "/");
            var uri = new Uri(baseUri, $"api/remote-agent/transaction-error-queue?machineCode={Uri.EscapeDataString(GetMachineCode())}");
            var client = _httpClientFactory.CreateClient("central-api");
            client.DefaultRequestHeaders.Remove("X-Agent-Key");
            client.DefaultRequestHeaders.Add("X-Agent-Key", GetAgentKey());

            using var response = await client.PostAsJsonAsync(uri, new TransactionErrorQueuePushRequest
            {
                Items = items
            }, JsonOptions, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Transaction error scanner pushed {Count} item(s). Response={Body}", items.Count, body);
                return;
            }

            _logger.LogWarning(
                "Transaction error scanner push failed. HTTP {StatusCode}. Body={Body}",
                response.StatusCode,
                body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transaction error scanner failed.");
        }
    }

    private static TransactionErrorQueuePushItem ToPushItem(TransactionListItem transaction)
    {
        var values = transaction.Values;
        var transactionId = GetString(values, "Id")
            ?? GetString(values, "TransactionId")
            ?? transaction.TransactionId;
        var transactionCode = GetString(values, "Code")
            ?? GetString(values, "TransactionCode")
            ?? transaction.TransactionId;

        return new TransactionErrorQueuePushItem
        {
            TransactionId = transactionId,
            TransactionCode = transactionCode,
            Status = GetInt(values, "Status"),
            Values = JsonSerializer.SerializeToElement(values, JsonOptions)
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> values, string key)
    {
        return values.TryGetValue(key, out var value) && value is not null
            ? Convert.ToString(value)
            : null;
    }

    private static int? GetInt(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private string GetMachineCode() => _agentOptions.MachineCode.Trim();

    private string GetAgentKey() => _agentOptions.AgentKey.Trim();
}
