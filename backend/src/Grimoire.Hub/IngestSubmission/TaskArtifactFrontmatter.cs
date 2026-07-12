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
    string? FailureReason,
    string? UserPromptSource = null,
    IReadOnlyDictionary<string, bool>? ConvertSteps = null)
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
            FailureReason: Unquote(frontmatter, "failure_reason"),
            UserPromptSource: Unquote(frontmatter, "user_prompt_source"),
            ConvertSteps: ParseConvertSteps(frontmatter));
    }

    /// <summary>
    /// Extracts the effective steering prompt from the artifact's `## User Prompt` body
    /// section (written by both HubTaskArtifactWriter and the agent's own writer). Tasks
    /// created before feature 004 have no such section — "defaults of their time".
    /// </summary>
    public static string? TryExtractUserPrompt(string markdown)
    {
        var idx = markdown.IndexOf("## User Prompt", StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var contentStart = idx + "## User Prompt".Length;
        var next = markdown.IndexOf("\n## ", contentStart, StringComparison.Ordinal);
        var section = next < 0 ? markdown[contentStart..] : markdown[contentStart..next];
        var trimmed = section.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static IReadOnlyDictionary<string, bool>? ParseConvertSteps(Dictionary<string, string> frontmatter)
    {
        if (!frontmatter.TryGetValue("convert_steps", out var raw) ||
            string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var steps = new Dictionary<string, bool>();
        foreach (var entry in raw.Trim().Trim('{', '}').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            steps[parts[0].Trim('"')] = string.Equals(parts[1], "enabled", StringComparison.OrdinalIgnoreCase);
        }

        return steps.Count == 0 ? null : steps;
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
