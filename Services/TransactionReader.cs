using FunStudio.WindowsMaintenance.Agent.Models;
using FunStudio.WindowsMaintenance.Agent.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace FunStudio.WindowsMaintenance.Agent.Services;

public sealed class TransactionReader
{
    private static readonly string[] SortColumns =
    [
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

    public TransactionReader(IOptions<TransactionOptions> options)
    {
        _options = options.Value;
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
