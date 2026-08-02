using Microsoft.Data.Sqlite;

namespace Grimoire.Hub.OperationalState;

public sealed class OperationalStateRepository
{
    /// <summary>
    /// hub_flags key pausing the remediation execution queue after a Hub restart
    /// (015-lint-board-parity T004, ADR-018/ADR-003) — own key, independent of ingest's
    /// <c>queue_paused</c> so the two domains' pause lifecycles never entangle (FR-015).
    /// Consumed by RemediationRunCoordinator once it exists (T032), same flag mechanism
    /// as <see cref="SetFlagAsync"/>/<see cref="GetFlagAsync"/>.
    /// </summary>
    public const string RemediationQueuePausedFlag = "remediation_queue_paused";

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
            CREATE TABLE IF NOT EXISTS remediation_tasks (
                task_id TEXT PRIMARY KEY,
                run_id TEXT NOT NULL,
                title TEXT NOT NULL,
                description TEXT NOT NULL,
                target_path TEXT NULL,
                state TEXT NOT NULL,
                proposed_at TEXT NOT NULL,
                authorized_at TEXT NULL,
                outcome_reason TEXT NULL,
                updated_at TEXT NOT NULL
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

    // ── Remediation Action Tasks (015-lint-board-parity T004, ADR-018/ADR-003) ────────

    public async Task InsertRemediationTaskAsync(RemediationTaskRow row, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO remediation_tasks(
                task_id, run_id, title, description, target_path,
                state, proposed_at, authorized_at, outcome_reason, updated_at)
            VALUES (
                $task_id, $run_id, $title, $description, $target_path,
                $state, $proposed_at, $authorized_at, $outcome_reason, $updated_at)
            ON CONFLICT(task_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$task_id", row.TaskId);
        command.Parameters.AddWithValue("$run_id", row.RunId);
        command.Parameters.AddWithValue("$title", row.Title);
        command.Parameters.AddWithValue("$description", row.Description);
        command.Parameters.AddWithValue("$target_path", (object?)row.TargetPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$state", row.State);
        command.Parameters.AddWithValue("$proposed_at", row.ProposedAt.ToString("O"));
        command.Parameters.AddWithValue("$authorized_at", (object?)row.AuthorizedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$outcome_reason", (object?)row.OutcomeReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", row.UpdatedAt.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// All remediation task rows, or only those in <paramref name="state"/> when given.
    /// Ordered by proposal time (queue order for the coordinator is <c>authorized_at</c>,
    /// derived by the caller from the <c>authorized</c> subset — FR-017).
    /// </summary>
    public async Task<IReadOnlyList<RemediationTaskRow>> GetRemediationTasksAsync(
        string? state = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT task_id, run_id, title, description, target_path,
                   state, proposed_at, authorized_at, outcome_reason, updated_at
            FROM remediation_tasks
            """
            + (state is null ? string.Empty : "\nWHERE state = $state")
            + "\nORDER BY proposed_at ASC, task_id ASC;";
        if (state is not null)
        {
            command.Parameters.AddWithValue("$state", state);
        }

        var results = new List<RemediationTaskRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new RemediationTaskRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                DateTimeOffset.Parse(reader.GetString(9))));
        }

        return results;
    }

    /// <summary>
    /// Compare-and-swap state transition on the persisted row — the single deterministic
    /// arbiter for every transition race (ADR-018: withdrawal vs. execution start, double
    /// terminal events): <c>UPDATE ... WHERE state = $from_state</c>, first commit wins,
    /// the loser sees <c>false</c> and must re-read the actual state. <c>authorized_at</c>
    /// is stamped from <paramref name="authorizedAt"/> when entering <c>authorized</c>
    /// (FIFO order authority, FR-017), cleared on withdrawal back to <c>proposed</c>
    /// (FR-016 — re-authorizing later gets a fresh queue position), and left untouched by
    /// every other transition. <paramref name="outcomeReason"/> is written as given
    /// (mandatory for <c>failed</c>/<c>not_applicable</c> — the state machine enforces
    /// that, T005; this row store does not re-judge it).
    /// </summary>
    public async Task<bool> TryTransitionRemediationTaskAsync(
        string taskId,
        string fromState,
        string toState,
        string? outcomeReason,
        DateTimeOffset? authorizedAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var stampsAuthorizedAt = toState == "authorized";
        var clearsAuthorizedAt = fromState == "authorized" && toState == "proposed";

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE remediation_tasks
            SET state = $to_state,
                outcome_reason = $outcome_reason,
                updated_at = $updated_at
            """
            + (stampsAuthorizedAt ? ",\n    authorized_at = $authorized_at" : string.Empty)
            + (clearsAuthorizedAt ? ",\n    authorized_at = NULL" : string.Empty)
            + "\nWHERE task_id = $task_id AND state = $from_state;";
        command.Parameters.AddWithValue("$task_id", taskId);
        command.Parameters.AddWithValue("$from_state", fromState);
        command.Parameters.AddWithValue("$to_state", toState);
        command.Parameters.AddWithValue("$outcome_reason", (object?)outcomeReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", updatedAt.ToString("O"));
        if (stampsAuthorizedAt)
        {
            command.Parameters.AddWithValue("$authorized_at", (object?)authorizedAt?.ToString("O") ?? DBNull.Value);
        }

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected == 1;
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
