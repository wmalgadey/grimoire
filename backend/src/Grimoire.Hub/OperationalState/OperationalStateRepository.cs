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
            CREATE TABLE IF NOT EXISTS ingest_queue (
                task_id TEXT PRIMARY KEY,
                accepted_at TEXT NOT NULL,
                source_ref TEXT NOT NULL,
                user_prompt TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS hub_flags (
                name TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Run Queue (ADR-008: persistent FIFO, ordered by acceptance time) ──────────

    public async Task EnqueueAsync(QueuedIngestRun run, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ingest_queue(task_id, accepted_at, source_ref, user_prompt)
            VALUES ($task_id, $accepted_at, $source_ref, $user_prompt)
            ON CONFLICT(task_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$task_id", run.TaskId);
        command.Parameters.AddWithValue("$accepted_at", run.AcceptedAt.ToString("O"));
        command.Parameters.AddWithValue("$source_ref", run.SourceRef);
        command.Parameters.AddWithValue("$user_prompt", (object?)run.UserPrompt ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QueuedIngestRun>> GetQueuedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT task_id, accepted_at, source_ref, user_prompt
            FROM ingest_queue
            ORDER BY accepted_at ASC, task_id ASC;
            """;

        var results = new List<QueuedIngestRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new QueuedIngestRun(
                reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return results;
    }

    public async Task RemoveQueuedAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ingest_queue WHERE task_id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Hub flags (queue_paused after restart, FR-021) ─────────────────────────────

    public async Task SetFlagAsync(string name, bool value, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO hub_flags(name, value) VALUES ($name, $value)
            ON CONFLICT(name) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$value", value ? "true" : "false");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> GetFlagAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM hub_flags WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
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
