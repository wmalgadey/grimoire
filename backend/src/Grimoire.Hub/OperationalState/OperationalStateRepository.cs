using Microsoft.Data.Sqlite;

namespace Grimoire.Hub.OperationalState;

public sealed class OperationalStateRepository
{
    private readonly string _connectionString;

    public OperationalStateRepository(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS operational_task_state (
                task_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                process_id INTEGER NULL,
                updated_at TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertAsync(OperationalTaskState state, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO operational_task_state(task_id, status, process_id, updated_at)
            VALUES ($task_id, $status, $process_id, $updated_at)
            ON CONFLICT(task_id) DO UPDATE SET
                status = excluded.status,
                process_id = excluded.process_id,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$task_id", state.TaskId);
        command.Parameters.AddWithValue("$status", state.Status);
        command.Parameters.AddWithValue("$process_id", (object?)state.ProcessId ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", state.UpdatedAt.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OperationalTaskState>> GetByStatusAsync(string status, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT task_id, status, process_id, updated_at
            FROM operational_task_state
            WHERE status = $status;
            """;
        command.Parameters.AddWithValue("$status", status);

        var results = new List<OperationalTaskState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new OperationalTaskState(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                DateTimeOffset.Parse(reader.GetString(3))));
        }

        return results;
    }

    public async Task DeleteAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM operational_task_state WHERE task_id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
