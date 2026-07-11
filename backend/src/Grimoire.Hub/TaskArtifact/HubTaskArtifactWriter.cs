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
        var directory = Path.GetDirectoryName(filePath) ?? ".";
        Directory.CreateDirectory(directory);
        var content = BuildMarkdown(document);

        // Write to a sibling temp file, then atomically rename it over the target. The board
        // projection (KanbanBoardProjectionStore) reads these task-artifact files while the pipeline
        // concurrently rewrites a stage; File.WriteAllText truncates in place, so a reader can catch
        // a half-written file, TryParse to null, and transiently drop the task from the board or 404
        // its detail (FR-007/FR-008). An atomic rename guarantees every reader sees either the whole
        // previous file or the whole new one — never a torn read. The temp name starts with '.' and
        // ends in '.tmp' so it is never picked up by the projection's "*.md" enumeration.
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, cancellationToken);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
            throw;
        }
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
