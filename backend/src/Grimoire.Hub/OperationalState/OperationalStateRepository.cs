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

    /// <summary>
    /// Busy-retry window (018-hub-cli-commands T012, research.md D1b): a CLI invocation and
    /// the running Hub process now write to the same operational-state database from two
    /// independent OS processes with no shared in-process lock. Rather than fail a writer
    /// immediately with <c>SQLITE_BUSY</c> when the other side holds the write lock, every
    /// connection sets SQLite's own retry-with-backoff handler (<c>PRAGMA busy_timeout</c>)
    /// so the loser waits up to this long before giving up.
    /// </summary>
    private static readonly TimeSpan BusyTimeout = TimeSpan.FromSeconds(5);

    private readonly string _connectionString;

    public OperationalStateRepository(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    /// <summary>
    /// Opens a connection hardened for concurrent Hub+CLI writers (018-hub-cli-commands
    /// T012, research.md D1b): WAL journal mode lets readers proceed without blocking on a
    /// writer, and <c>busy_timeout</c> makes a writer that loses the race retry with
    /// backoff instead of throwing <c>SQLITE_BUSY</c> immediately. Both are per-connection
    /// PRAGMAs (WAL's mode is persisted in the database file after the first connection
    /// enables it, but is re-asserted here defensively; busy_timeout is always
    /// per-connection) — no schema change.
    /// </summary>
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText =
            $"""
            PRAGMA journal_mode = 'WAL';
            PRAGMA busy_timeout = {(int)BusyTimeout.TotalMilliseconds};
            """;
        await pragmaCommand.ExecuteNonQueryAsync(cancellationToken);

        return connection;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS operational_task_state (
                task_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                process_id INTEGER NULL,
                updated_at TEXT NOT NULL,
                attempt INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS ingest_status_history (
                task_id TEXT NOT NULL,
                seq INTEGER NOT NULL,
                status TEXT NOT NULL,
                entered_at TEXT NOT NULL,
                detail TEXT NULL,
                PRIMARY KEY (task_id, seq)
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

    // ── Status history (023-task-ui-improvements, ADR-025, data-model.md §1) ──────

    /// <summary>
    /// Appends one transition to a task's append-only history (FR-005). <c>seq</c> is
    /// derived inside the INSERT from the task's own current maximum, so two concurrent
    /// appends cannot compute the same number and silently overwrite one another: the
    /// composite primary key rejects a duplicate outright rather than clobbering a row.
    /// Returns the assigned sequence number.
    /// </summary>
    public async Task<long> AppendStatusHistoryAsync(
        string taskId, string status, DateTimeOffset enteredAt, string? detail = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ingest_status_history(task_id, seq, status, entered_at, detail)
            SELECT $task_id,
                   COALESCE((SELECT MAX(seq) FROM ingest_status_history WHERE task_id = $task_id), 0) + 1,
                   $status, $entered_at, $detail
            RETURNING seq;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$entered_at", enteredAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);

        var seq = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(seq);
    }

    /// <summary>
    /// A task's full history in <c>seq</c> order (FR-006). Empty for a task that predates
    /// this feature — the detail view falls back to rendering its current status as a
    /// single entry rather than treating the absence as an error.
    /// </summary>
    public async Task<IReadOnlyList<IngestStatusHistoryEntry>> GetStatusHistoryAsync(
        string taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT task_id, seq, status, entered_at, detail
            FROM ingest_status_history
            WHERE task_id = $task_id
            ORDER BY seq ASC;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);

        var results = new List<IngestStatusHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new IngestStatusHistoryEntry(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return results;
    }

    // ── Run Queue (ADR-008: persistent FIFO, ordered by acceptance time) ──────────

    public async Task EnqueueAsync(QueuedIngestRun run, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

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
        await using var connection = await OpenConnectionAsync(cancellationToken);

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
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ingest_queue WHERE task_id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Hub flags (queue_paused after restart, FR-021) ─────────────────────────────

    public async Task SetFlagAsync(string name, bool value, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

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
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM hub_flags WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    // ── Remediation Action Tasks (015-lint-board-parity T004, ADR-018/ADR-003) ────────

    public async Task InsertRemediationTaskAsync(RemediationTaskRow row, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

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
        await using var connection = await OpenConnectionAsync(cancellationToken);

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
        await using var connection = await OpenConnectionAsync(cancellationToken);

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
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO operational_task_state(task_id, status, process_id, updated_at, attempt)
            VALUES ($task_id, $status, $process_id, $updated_at, $attempt)
            ON CONFLICT(task_id) DO UPDATE SET
                status = excluded.status,
                process_id = excluded.process_id,
                updated_at = excluded.updated_at,
                attempt = excluded.attempt;
            """;
        command.Parameters.AddWithValue("$task_id", state.TaskId);
        command.Parameters.AddWithValue("$status", state.Status);
        command.Parameters.AddWithValue("$process_id", (object?)state.ProcessId ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", state.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$attempt", state.Attempt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 023-task-ui-improvements T029 (FR-012/SC-008): the restart race's deterministic
    /// arbiter. An <c>INSERT … ON CONFLICT DO NOTHING</c> either creates the task's
    /// operational row (this caller won the restart) or affects nothing (another restart
    /// already claimed it, or the task is queued/running and was never eligible). Same
    /// first-commit-wins CAS idiom as
    /// <see cref="TryTransitionRemediationTaskAsync"/> (ADR-018), applied to a row whose
    /// absence — not a state value — is what marks a task as restartable.
    /// </summary>
    public async Task<bool> TryClaimTaskStateAsync(OperationalTaskState state, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO operational_task_state(task_id, status, process_id, updated_at, attempt)
            VALUES ($task_id, $status, $process_id, $updated_at, $attempt)
            ON CONFLICT(task_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$task_id", state.TaskId);
        command.Parameters.AddWithValue("$status", state.Status);
        command.Parameters.AddWithValue("$process_id", (object?)state.ProcessId ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", state.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$attempt", state.Attempt);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<OperationalTaskState>> GetByStatusAsync(string status, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT task_id, status, process_id, updated_at, attempt
            FROM operational_task_state
            WHERE status = $status;
            """;
        command.Parameters.AddWithValue("$status", status);

        var results = new List<OperationalTaskState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadTaskState(reader));
        }

        return results;
    }

    /// <summary>One task's operational row, or null when it holds no run slot right now.</summary>
    public async Task<OperationalTaskState?> GetByTaskIdAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT task_id, status, process_id, updated_at, attempt
            FROM operational_task_state
            WHERE task_id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTaskState(reader) : null;
    }

    private static OperationalTaskState ReadTaskState(System.Data.Common.DbDataReader reader) =>
        new(reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            DateTimeOffset.Parse(reader.GetString(3)),
            reader.IsDBNull(4) ? 0 : reader.GetInt32(4));

    public async Task DeleteAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM operational_task_state WHERE task_id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
