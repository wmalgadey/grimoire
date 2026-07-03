namespace Grimoire.IngestAgent.IngestLog;

public sealed class IngestLogAppender
{
    public async Task AppendAsync(string logPath, string outcome, string sourceRef, string detail, string taskId, CancellationToken cancellationToken)
    {
        var line = $"## [{DateTime.UtcNow:yyyy-MM-dd}] ingest | {outcome} | source: {sourceRef} | {detail} | task: {taskId}{Environment.NewLine}";
        await File.AppendAllTextAsync(logPath, line, cancellationToken);
    }
}
