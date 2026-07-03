namespace Grimoire.Hub.OperationalState;

public sealed class RestartReconciler
{
    private readonly OperationalStateRepository _repository;

    public RestartReconciler(OperationalStateRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> ReconcileRunningTasksAsync(string tasksDir, string logPath, CancellationToken cancellationToken = default)
    {
        var running = await _repository.GetByStatusAsync("running", cancellationToken);
        foreach (var state in running)
        {
            var reason = "Hub restarted while task was running.";

            // Delete the stale row; task artifact + log.md are the durable record (ADR-003).
            await _repository.DeleteAsync(state.TaskId, cancellationToken);

            await UpdateTaskArtifactAsync(tasksDir, state.TaskId, reason, cancellationToken);
            await AppendReconciliationLogAsync(logPath, state.TaskId, cancellationToken);
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

    private static async Task AppendReconciliationLogAsync(string logPath, string taskId, CancellationToken cancellationToken)
    {
        var line = $"## [{DateTime.UtcNow:yyyy-MM-dd}] ingest | failed | reconciled on startup | task: [[tasks/{taskId}.md]]{Environment.NewLine}";
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
