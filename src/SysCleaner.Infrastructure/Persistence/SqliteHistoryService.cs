using Microsoft.Data.Sqlite;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Domain.Models;

namespace SysCleaner.Infrastructure.Persistence;

public sealed class SqliteHistoryService : IHistoryService
{
    private readonly string _connectionString;

    public SqliteHistoryService()
    {
        var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysCleaner");
        Directory.CreateDirectory(basePath);
        var dbPath = Path.Combine(basePath, "syscleaner.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS operation_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                module TEXT NOT NULL,
                action TEXT NOT NULL,
                target TEXT NOT NULL,
                result TEXT NOT NULL,
                details TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task LogAsync(OperationLogEntry entry, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO operation_log(timestamp, module, action, target, result, details) VALUES ($timestamp, $module, $action, $target, $result, $details)";
        command.Parameters.AddWithValue("$timestamp", entry.Timestamp.ToString("O"));
        command.Parameters.AddWithValue("$module", entry.Module);
        command.Parameters.AddWithValue("$action", entry.Action);
        command.Parameters.AddWithValue("$target", entry.Target);
        command.Parameters.AddWithValue("$result", entry.Result);
        command.Parameters.AddWithValue("$details", entry.Details);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OperationLogEntry>> GetRecentAsync(int take = 200, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var results = new List<OperationLogEntry>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id, timestamp, module, action, target, result, details FROM operation_log ORDER BY id DESC LIMIT $take";
        command.Parameters.AddWithValue("$take", take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new OperationLogEntry(
                reader.GetInt64(0),
                DateTime.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6)));
        }

        return results;
    }
}