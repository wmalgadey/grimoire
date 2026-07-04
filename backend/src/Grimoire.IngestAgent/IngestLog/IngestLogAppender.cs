namespace Grimoire.IngestAgent.IngestLog;

public sealed class IngestLogAppender
{
    public record DeniedLogEntry(string Action, string TargetPath, string Reason, string? PolicyRule);

    public async Task AppendAsync(string logPath, string outcome, string sourceRef, string detail, string taskId, CancellationToken cancellationToken)
    {
        var line = $"## [{DateTime.UtcNow:yyyy-MM-dd}] ingest | {outcome} | source: {sourceRef} | {detail} | task: [[tasks/{taskId}.md]]{Environment.NewLine}";

        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.append_log");
        span?.SetTag("outcome", outcome);

        await File.AppendAllTextAsync(logPath, line, cancellationToken);
    }

    public async Task AppendDeniedActionAsync(string logPath, string taskId, DeniedLogEntry denied, CancellationToken cancellationToken)
    {
        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.log_denied_action");
        span?.SetTag("task_id", taskId);
        span?.SetTag("action", denied.Action);

        var policyText = string.IsNullOrWhiteSpace(denied.PolicyRule) ? "none" : denied.PolicyRule;
        var line =
            $"## [{DateTime.UtcNow:yyyy-MM-dd}] ingest.guardrail.action_denied | action={denied.Action} | target={denied.TargetPath} | reason={denied.Reason} | policy_rule={policyText} | task: [[tasks/{taskId}.md]]{Environment.NewLine}";

        await File.AppendAllTextAsync(logPath, line, cancellationToken);
    }

    public async Task AppendCompletionSummaryAsync(
        string logPath,
        string taskId,
        int createdCount,
        int updatedCount,
        int supersededCount,
        int deniedCount,
        CancellationToken cancellationToken)
    {
        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.log_completion_summary");
        span?.SetTag("task_id", taskId);

        var line =
            $"## [{DateTime.UtcNow:yyyy-MM-dd}] ingest.wiki.structure.completed | created={createdCount} | updated={updatedCount} | superseded={supersededCount} | denied={deniedCount} | task: [[tasks/{taskId}.md]]{Environment.NewLine}";

        await File.AppendAllTextAsync(logPath, line, cancellationToken);
    }
}
