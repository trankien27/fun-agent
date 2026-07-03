using FunStudio.WindowsMaintenance.Agent.Models;
using FunStudio.WindowsMaintenance.Agent.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace FunStudio.WindowsMaintenance.Agent.Services;

public sealed class TransactionReader
{
    private static readonly string[] SortColumns =
    [
        "RecordAt",
        "CreatedAt",
        "CreatedTime",
        "CreatedDate",
        "TransactionDate",
        "TransactionTime",
        "UpdatedAt",
        "UpdatedTime",
        "Id",
        "id"
    ];

    private static readonly string[] DateColumns =
    [
        "RecordAt",
        "CreatedAt",
        "CreatedTime",
        "CreatedDate",
        "TransactionDate",
        "TransactionTime",
        "UpdatedAt",
        "UpdatedTime"
    ];

    private static readonly string[] TransactionIdColumns =
    [
        "TransactionId",
        "transactionId",
        "TransactionID",
        "TransactionCode",
        "Code",
        "Id",
        "id"
    ];

    private readonly TransactionOptions _options;
    private readonly ILogger<TransactionReader> _logger;

    public TransactionReader(IOptions<TransactionOptions> options, ILogger<TransactionReader> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<TransactionListItem>> GetTransactionsAsync(CancellationToken cancellationToken)
    {
        SQLitePCL.Batteries_V2.Init();

        if (string.IsNullOrWhiteSpace(_options.DatabasePath))
        {
            throw new InvalidOperationException("Transactions:DatabasePath is required.");
        }

        if (!File.Exists(_options.DatabasePath))
        {
            throw new FileNotFoundException("SQLite database file was not found.", _options.DatabasePath);
        }

        var items = new List<TransactionListItem>();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var query = string.IsNullOrWhiteSpace(_options.Query)
            ? await BuildDefaultQueryAsync(connection, cancellationToken)
            : _options.Query;

        await using var command = connection.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = 30;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = await reader.IsDBNullAsync(index, cancellationToken)
                    ? null
                    : reader.GetValue(index);

                values[reader.GetName(index)] = value;
            }

            items.Add(new TransactionListItem
            {
                TransactionId = ResolveTransactionId(values),
                Values = values
            });
        }

        return items;
    }

    public async Task<IReadOnlyCollection<TransactionListItem>> GetTodayErrorTransactionsAsync(
        int successStatus,
        int limit,
        CancellationToken cancellationToken)
    {
        SQLitePCL.Batteries_V2.Init();

        if (string.IsNullOrWhiteSpace(_options.DatabasePath))
        {
            throw new InvalidOperationException("Transactions:DatabasePath is required.");
        }

        if (!File.Exists(_options.DatabasePath))
        {
            throw new FileNotFoundException("SQLite database file was not found.", _options.DatabasePath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var query = await BuildTodayErrorQueryAsync(connection, successStatus, limit, cancellationToken);
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var items = new List<TransactionListItem>();
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = 30;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = await reader.IsDBNullAsync(index, cancellationToken)
                    ? null
                    : reader.GetValue(index);

                values[reader.GetName(index)] = value;
            }

            items.Add(new TransactionListItem
            {
                TransactionId = ResolveTransactionId(values),
                Values = values
            });
        }

        return items;
    }

    private async Task<string> BuildDefaultQueryAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var tableName = string.IsNullOrWhiteSpace(_options.TableName) ? "Transactions" : _options.TableName;
        var columns = await GetTableColumnsAsync(connection, tableName, cancellationToken);
        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Table '{tableName}' was not found or has no columns.");
        }

        var sortColumn = SortColumns.FirstOrDefault(column => columns.Contains(column));
        var limit = _options.Limit <= 0 ? 200 : _options.Limit;
        var query = $"SELECT * FROM {QuoteIdentifier(tableName)}";

        if (!string.IsNullOrWhiteSpace(sortColumn))
        {
            query += $" ORDER BY {QuoteIdentifier(sortColumn)} DESC";
        }

        query += $" LIMIT {limit}";
        return query;
    }

    private async Task<string?> BuildTodayErrorQueryAsync(
        SqliteConnection connection,
        int successStatus,
        int limit,
        CancellationToken cancellationToken)
    {
        var tableName = string.IsNullOrWhiteSpace(_options.TableName) ? "Transactions" : _options.TableName;
        var columns = await GetTableColumnsAsync(connection, tableName, cancellationToken);
        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Table '{tableName}' was not found or has no columns.");
        }

        var statusColumn = FindColumn(columns, "Status");
        if (string.IsNullOrWhiteSpace(statusColumn))
        {
            _logger.LogWarning("Transaction error scanner skipped because table {TableName} has no Status column.", tableName);
            return null;
        }

        var dateColumn = DateColumns
            .Select(column => FindColumn(columns, column))
            .FirstOrDefault(column => !string.IsNullOrWhiteSpace(column));
        if (string.IsNullOrWhiteSpace(dateColumn))
        {
            _logger.LogWarning("Transaction error scanner skipped because table {TableName} has no supported date column.", tableName);
            return null;
        }

        var take = limit <= 0 ? 200 : limit;
        return $"SELECT * FROM {QuoteIdentifier(tableName)} " +
               $"WHERE {QuoteIdentifier(statusColumn)} <> {successStatus} " +
               $"AND date({QuoteIdentifier(dateColumn)}) = date('now', 'localtime') " +
               $"ORDER BY {QuoteIdentifier(dateColumn)} DESC " +
               $"LIMIT {take}";
    }

    private static string? FindColumn(IEnumerable<string> columns, string columnName)
    {
        return columns.FirstOrDefault(column => string.Equals(column, columnName, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<HashSet<string>> GetTableColumnsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteString(tableName)})";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader["name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                columns.Add(name);
            }
        }

        return columns;
    }

    private static string QuoteIdentifier(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string QuoteString(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

    private static string ResolveTransactionId(IReadOnlyDictionary<string, object?> values)
    {
        foreach (var column in TransactionIdColumns)
        {
            if (values.TryGetValue(column, out var value) && value is not null)
            {
                return Convert.ToString(value) ?? "";
            }
        }

        return "";
    }
}
