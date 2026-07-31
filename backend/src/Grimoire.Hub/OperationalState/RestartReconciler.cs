using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Hub.OperationalState;

public sealed class RestartReconciler
{
    private readonly OperationalStateRepository _repository;
    private readonly ILogger<RestartReconciler> _logger;

    public RestartReconciler(OperationalStateRepository repository, ILogger<RestartReconciler>? logger = null)
    {
        _repository = repository;
        _logger = logger ?? NullLogger<RestartReconciler>.Instance;
    }

    public async Task<int> ReconcileRunningTasksAsync(string tasksDir, string logPath, CancellationToken cancellationToken = default)
    {
        var running = await _repository.GetByStatusAsync("running", cancellationToken);
        foreach (var state in running)
        {
            var reason = "Hub restarted while task was running.";

            await UpdateTaskArtifactAsync(tasksDir, state.TaskId, reason, cancellationToken);
            await AppendReconciliationLogAsync(logPath, state.TaskId, cancellationToken);

            // Delete the stale row after durable writes succeed; task artifact + log.md are
            // the permanent record (ADR-003). Deleting last keeps the row retryable if either
            // write above fails on a transient IO error.
            await _repository.DeleteAsync(state.TaskId, cancellationToken);

            HubMetrics.RecordTaskReconciled();
            using var reconciledSpan = HubTracing.ActivitySource.StartActivity("ingest.task.reconciled");
            reconciledSpan?.SetTag("signal_type", "log");
            reconciledSpan?.SetTag("event_name", "ingest.task.reconciled");
            reconciledSpan?.SetTag("level", "Warning");
            reconciledSpan?.SetTag("task_id", state.TaskId);
            reconciledSpan?.SetTag("interruption_reason", reason);

            _logger.LogWarning(new EventId(10, "ingest.task.reconciled"),
                "Task {task_id} reconciled on Hub restart. Reason: {interruption_reason}",
                state.TaskId, reason);
        }

        return running.Count;
    }

    private static async Task UpdateTaskArtifactAsync(string tasksDir, string taskId, string reason, CancellationToken cancellationToken)
    {
        var taskPath = Path.Combine(tasksDir, $"{taskId}.md");
        if (!File.Exists(taskPath))
        {
            return;
        }

        var text = await File.ReadAllTextAsync(taskPath, cancellationToken);
        text = ReplaceOrAppendFrontmatterValue(text, "status", "failed");
        text = ReplaceOrAppendFrontmatterValue(text, "completed_at", DateTimeOffset.UtcNow.ToString("O"));
        text = ReplaceOrAppendFrontmatterValue(text, "failure_reason", $"\"{reason}\"");
        text = ReplaceOrAppendFrontmatterValue(text, "pages_touched", "[]");

        await File.WriteAllTextAsync(taskPath, text, cancellationToken);
    }

    /// <summary>
    /// 014-wiki-storage-restructure (ADR-017, FR-007/FR-008/FR-011): this is a plain
    /// direct-file-I/O write outside the guarded tool boundary (Hub-owned operational
    /// recovery, not an agent tool call), so ADR-017's structural check never runs
    /// against it — but SC-003's "any agent or the backstop" guarantee still requires the
    /// same heading-plus-paragraph shape as every other log.md entry
    /// (contracts/log-and-catalog-entry-format.md), so this composes the line by hand in
    /// that shape rather than the pre-014 single pipe-delimited line.
    /// </summary>
    private static async Task AppendReconciliationLogAsync(string logPath, string taskId, CancellationToken cancellationToken)
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var paragraph =
            $"Harness backstop entry: task {taskId} was still running when the Hub restarted, so it was reconciled as failed on startup. Task: [[tasks/{taskId}.md]].";
        var line = $"## [{date}] ingest | failed (reconciled on startup){Environment.NewLine}{Environment.NewLine}{paragraph}{Environment.NewLine}";
        await File.AppendAllTextAsync(logPath, line, cancellationToken);
    }

    private static string ReplaceOrAppendFrontmatterValue(string content, string key, string value)
    {
        var lines = content.Split('\n').ToList();
        if (lines.Count < 3 || lines[0].Trim() != "---")
        {
            return content;
        }

        var end = lines.FindIndex(1, l => l.Trim() == "---");
        if (end < 0)
        {
            return content;
        }

        var keyPrefix = key + ":";
        var idx = lines.FindIndex(1, end - 1, l => l.TrimStart().StartsWith(keyPrefix, StringComparison.Ordinal));
        var replacement = $"{key}: {value}";
        if (idx >= 0)
        {
            lines[idx] = replacement;
        }
        else
        {
            lines.Insert(end, replacement);
        }

        return string.Join("\n", lines);
    }
}
