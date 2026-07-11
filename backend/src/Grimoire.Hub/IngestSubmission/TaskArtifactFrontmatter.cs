namespace Grimoire.Hub.IngestSubmission;

/// <summary>
/// Minimal, tolerant read of a Task Artifact markdown file's frontmatter
/// (contracts/task-artifact-format.md). Deliberately independent of
/// <c>Grimoire.IngestAgent.TaskArtifact.TaskArtifactDocument</c> (T001 boundary): the board only
/// needs a handful of fields and must be able to read files written by either
/// <c>HubTaskArtifactWriter</c> (pre-agent stages) or the Ingest agent's own writer
/// (agent-owned stages), which share the same frontmatter shape but not the same type.
/// </summary>
public sealed record TaskArtifactFrontmatter(
    string TaskId,
    string Status,
    DateTimeOffset StartedAt,
    string? SourceRef,
    string? OriginalRef,
    string? FailureReason)
{
    public static TaskArtifactFrontmatter? TryParse(string markdown)
    {
        var sections = markdown.Split("---", 3, StringSplitOptions.None);
        if (sections.Length < 3)
        {
            return null;
        }

        var frontmatter = sections[1]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);

        if (!frontmatter.TryGetValue("task_id", out var taskId) || !frontmatter.TryGetValue("status", out var status))
        {
            return null;
        }

        var startedAt = frontmatter.TryGetValue("started_at", out var startedRaw) && DateTimeOffset.TryParse(startedRaw, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

        return new TaskArtifactFrontmatter(
            TaskId: taskId,
            Status: status,
            StartedAt: startedAt,
            SourceRef: Unquote(frontmatter, "source_ref"),
            OriginalRef: Unquote(frontmatter, "original_ref"),
            FailureReason: Unquote(frontmatter, "failure_reason"));
    }

    private static string? Unquote(Dictionary<string, string> frontmatter, string key)
    {
        if (!frontmatter.TryGetValue(key, out var raw) || string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return raw.Trim().Trim('"');
    }
}
