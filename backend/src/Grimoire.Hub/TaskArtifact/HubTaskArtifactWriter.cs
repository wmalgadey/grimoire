using System.Text;

namespace Grimoire.Hub.TaskArtifact;

/// <summary>
/// Creates and updates the Task Artifact markdown file for the Hub-owned pre-agent stages
/// (FR-006, data-model.md TaskArtifact). Writes the full v2 frontmatter shape from
/// contracts/task-artifact-format.md (agent-owned fields default to their "not yet started"
/// values) so the file is a valid artifact immediately, and so the Ingest agent's own writer
/// can safely overwrite it once triggered (ADR-002: each process owns its own artifact I/O).
/// </summary>
public sealed class HubTaskArtifactWriter
{
    public async Task WriteAsync(string filePath, HubTaskArtifactDocument document, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        var content = BuildMarkdown(document);
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
    }

    private static string BuildMarkdown(HubTaskArtifactDocument doc)
    {
        var completedAt = doc.CompletedAt.HasValue ? doc.CompletedAt.Value.ToString("O") : "null";
        var failureFirstLine = string.IsNullOrWhiteSpace(doc.FailureReason) ? doc.FailureReason : doc.FailureReason.Split('\n')[0];
        var failure = string.IsNullOrWhiteSpace(failureFirstLine) ? "null" : $"\"{Escape(failureFirstLine)}\"";
        var sourceRef = doc.SourceRef is null ? "null" : $"\"{Escape(doc.SourceRef)}\"";
        var originalRef = doc.OriginalRef is null ? "null" : $"\"{Escape(doc.OriginalRef)}\"";

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"task_id: {doc.TaskId}");
        sb.AppendLine("type: ingest");
        sb.AppendLine($"status: {doc.Status}");
        sb.AppendLine("agent: ingest");
        sb.AppendLine($"started_at: {doc.StartedAt:O}");
        sb.AppendLine($"completed_at: {completedAt}");
        sb.AppendLine($"source_ref: {sourceRef}");
        sb.AppendLine($"original_ref: {originalRef}");
        sb.AppendLine("pages_touched: []");
        sb.AppendLine("pages_created: []");
        sb.AppendLine("pages_updated: []");
        sb.AppendLine("pages_superseded: []");
        sb.AppendLine("denied_actions: []");
        sb.AppendLine("instruction_files: []");
        sb.AppendLine("policy: null");
        sb.AppendLine("model: null");
        sb.AppendLine("turns: null");
        sb.AppendLine("rolled_back: null");
        sb.AppendLine($"failure_reason: {failure}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(doc.Narrative.TrimEnd());
        sb.AppendLine();
        return sb.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
