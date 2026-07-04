namespace Grimoire.IngestAgent.IngestLog;

/// <summary>
/// Harness backstop for the ingest log (R8). On success the agent is expected
/// to append the log entry via <c>write_file</c>. This appender verifies that
/// an entry for the task id exists; if absent — and always on failure — it
/// appends a minimal factual entry and emits <c>ingest.log.backstop_appended</c>.
/// </summary>
public sealed class IngestLogAppender
{
    /// <summary>
    /// Ensures a log entry exists for <paramref name="taskId"/>.
    /// Always appends on failure; on success only appends if the agent omitted the entry.
    /// </summary>
    public async Task EnsureLogEntryAsync(
        string logPath,
        string outcome,
        string sourceRef,
        string taskId,
        bool forceAppend,
        CancellationToken cancellationToken)
    {
        var backstopNeeded = forceAppend;

        if (!backstopNeeded && File.Exists(logPath))
        {
            var logContent = await File.ReadAllTextAsync(logPath, cancellationToken);
            backstopNeeded = !logContent.Contains(taskId, StringComparison.Ordinal);
        }
        else if (!backstopNeeded)
        {
            // log.md doesn't exist yet — backstop needed.
            backstopNeeded = true;
        }

        if (!backstopNeeded)
            return;

        var line = $"## [{DateTime.UtcNow:yyyy-MM-dd}] ingest | {outcome} | source: {sourceRef} | backstop entry | task: [[tasks/{taskId}.md]]{Environment.NewLine}";

        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.backstop_log");
        span?.SetTag("task_id", taskId);
        span?.SetTag("outcome", outcome);

        await File.AppendAllTextAsync(logPath, line, cancellationToken);

        IngestAgentMetrics.RecordIngest(outcome, 0, "none", 0);
    }

    /// <summary>
    /// Legacy overload for compatibility with Hub restart reconciliation path.
    /// </summary>
    public async Task AppendAsync(
        string logPath,
        string outcome,
        string sourceRef,
        string detail,
        string taskId,
        CancellationToken cancellationToken)
    {
        var line = $"## [{DateTime.UtcNow:yyyy-MM-dd}] ingest | {outcome} | source: {sourceRef} | {detail} | task: [[tasks/{taskId}.md]]{Environment.NewLine}";

        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.append_log");
        span?.SetTag("outcome", outcome);

        await File.AppendAllTextAsync(logPath, line, cancellationToken);
    }
}

