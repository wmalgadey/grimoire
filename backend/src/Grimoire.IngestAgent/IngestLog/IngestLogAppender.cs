namespace Grimoire.IngestAgent.IngestLog;

public sealed class IngestLogAppender
{
    public async Task AppendAsync(string logPath, string outcome, string sourceRef, string detail, string taskId, CancellationToken cancellationToken)
    {
        var line = $"## [{DateTime.UtcNow:yyyy-MM-dd}] ingest | {outcome} | source: {sourceRef} | {detail} | task: [[tasks/{taskId}.md]]{Environment.NewLine}";

        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.append_log");
        span?.SetTag("outcome", outcome);

        await File.AppendAllTextAsync(logPath, line, cancellationToken);
    }
}
