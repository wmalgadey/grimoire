using System.Globalization;
using System.Text;

namespace Grimoire.IngestAgent.TaskArtifact;

public sealed class TaskArtifactStore
{
    public async Task WriteAsync(string filePath, TaskArtifactDocument document, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        var content = BuildMarkdown(document);
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
    }

    public async Task<TaskArtifactDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken);
        return ParseMarkdown(text);
    }

    private static string BuildMarkdown(TaskArtifactDocument doc)
    {
        var completedAt = doc.CompletedAt.HasValue ? doc.CompletedAt.Value.ToString("O") : "null";
        var failure = string.IsNullOrWhiteSpace(doc.FailureReason) ? "null" : $"\"{Escape(doc.FailureReason)}\"";
        var pages = doc.PagesTouched.Count == 0 ? "[]" : $"[{string.Join(", ", doc.PagesTouched.Select(p => $"\"{Escape(p)}\""))}]";

        return $"---\n" +
               $"task_id: {doc.TaskId}\n" +
               $"type: {doc.Type}\n" +
               $"status: {doc.Status}\n" +
               $"agent: {doc.Agent}\n" +
               $"started_at: {doc.StartedAt:O}\n" +
               $"completed_at: {completedAt}\n" +
               $"source_ref: \"{Escape(doc.SourceRef)}\"\n" +
               $"pages_touched: {pages}\n" +
               $"failure_reason: {failure}\n" +
               $"---\n\n" +
               doc.Narrative.TrimEnd() + "\n";
    }

    private static TaskArtifactDocument ParseMarkdown(string markdown)
    {
        var sections = markdown.Split("---", StringSplitOptions.None);
        if (sections.Length < 3)
        {
            throw new InvalidOperationException("Task artifact markdown has invalid frontmatter.");
        }

        var frontmatter = sections[1]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);

        static string Unquote(string value) => value.Trim().Trim('"');

        var pagesTouched = frontmatter.TryGetValue("pages_touched", out var pagesRaw) && pagesRaw != "[]"
            ? pagesRaw.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote).ToList()
            : new List<string>();

        DateTimeOffset? completedAt = frontmatter.TryGetValue("completed_at", out var completedAtRaw) && !string.Equals(completedAtRaw, "null", StringComparison.OrdinalIgnoreCase)
            ? DateTimeOffset.Parse(completedAtRaw, CultureInfo.InvariantCulture)
            : null;

        var failureReason = frontmatter.TryGetValue("failure_reason", out var failureRaw) && !string.Equals(failureRaw, "null", StringComparison.OrdinalIgnoreCase)
            ? Unquote(failureRaw)
            : null;

        return new TaskArtifactDocument(
            TaskId: frontmatter["task_id"],
            Type: frontmatter["type"],
            Status: frontmatter["status"],
            Agent: frontmatter["agent"],
            StartedAt: DateTimeOffset.Parse(frontmatter["started_at"], CultureInfo.InvariantCulture),
            CompletedAt: completedAt,
            SourceRef: Unquote(frontmatter["source_ref"]),
            PagesTouched: pagesTouched,
            FailureReason: failureReason,
            Narrative: sections[2].Trim());
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
