using System.Globalization;
using System.Text;
using System.Text.Json;

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
        var failureFirstLine = string.IsNullOrWhiteSpace(doc.FailureReason) ? doc.FailureReason : doc.FailureReason.Split('\n')[0];
        var failure = string.IsNullOrWhiteSpace(failureFirstLine) ? "null" : $"\"{Escape(failureFirstLine)}\"";
        // Keep pages_touched for backward compat (Hub reconciliation reads it).
        var pagesTouched = BuildStringList(doc.PagesTouched);

        // 023 (FR-003): quoted so a label containing ':' or '"' cannot corrupt the
        // frontmatter; `null` when the run was launched without one.
        var title = string.IsNullOrWhiteSpace(doc.Title) ? "null" : $"\"{Escape(doc.Title)}\"";

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"task_id: {doc.TaskId}");
        sb.AppendLine($"title: {title}");
        sb.AppendLine($"type: {doc.Type}");
        sb.AppendLine($"status: {doc.Status}");
        sb.AppendLine($"agent: {doc.Agent}");
        sb.AppendLine($"started_at: {doc.StartedAt:O}");
        sb.AppendLine($"completed_at: {completedAt}");
        sb.AppendLine($"source_ref: \"{Escape(doc.SourceRef)}\"");
        sb.AppendLine($"pages_touched: {pagesTouched}");
        sb.AppendLine($"pages_created: {BuildStringList(doc.PagesCreated)}");
        sb.AppendLine($"pages_updated: {BuildStringList(doc.PagesUpdated)}");
        sb.AppendLine($"pages_superseded: {BuildStringList(doc.PagesSuperseded)}");
        sb.AppendLine($"denied_actions: {BuildDeniedActionsJson(doc.DeniedActions)}");
        sb.AppendLine($"instruction_files: {BuildInstructionFilesJson(doc.InstructionFiles)}");
        sb.AppendLine($"policy: {BuildPolicyJson(doc.Policy)}");
        sb.AppendLine($"model: {(doc.Model is null ? "null" : $"\"{Escape(doc.Model)}\"")}");
        sb.AppendLine($"turns: {(doc.Turns.HasValue ? doc.Turns.Value.ToString() : "null")}");
        sb.AppendLine($"rolled_back: {(doc.RolledBack.HasValue ? (doc.RolledBack.Value ? "true" : "false") : "null")}");
        sb.AppendLine($"user_prompt_source: {(doc.UserPromptSource is null ? "null" : doc.UserPromptSource)}");
        sb.AppendLine($"convert_steps: {BuildConvertSteps(doc.ConvertSteps)}");
        sb.AppendLine($"failure_reason: {failure}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(doc.Narrative.TrimEnd());
        sb.AppendLine();

        // 004 (FR-009): the effective steering prompt is recorded verbatim as a body
        // section so task details can display it without frontmatter size limits.
        if (!string.IsNullOrWhiteSpace(doc.UserPrompt))
        {
            sb.AppendLine();
            sb.AppendLine("## User Prompt");
            sb.AppendLine();
            sb.AppendLine(doc.UserPrompt.TrimEnd());
        }

        // Contract (task-artifact-format.md): completed artifacts with denials carry a
        // human-readable body section mirroring the denied_actions frontmatter.
        if (doc.Status == "completed" && doc.DeniedActions is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("## Denied actions");
            sb.AppendLine();
            foreach (var denial in doc.DeniedActions)
            {
                sb.AppendLine(
                    $"- `{denial.Action}` on `{denial.RequestedTarget}` " +
                    $"(canonical: `{denial.CanonicalTarget}`) — denied: {denial.Reason} (turn {denial.Turn})");
            }
        }

        return sb.ToString();
    }

    private static string BuildConvertSteps(IReadOnlyDictionary<string, bool>? steps)
    {
        if (steps is null || steps.Count == 0) return "null";
        var entries = steps.OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => $"\"{Escape(s.Key)}\": {(s.Value ? "enabled" : "disabled")}");
        return "{" + string.Join(", ", entries) + "}";
    }

    private static IReadOnlyDictionary<string, bool>? ParseConvertSteps(Dictionary<string, string> fm)
    {
        if (!fm.TryGetValue("convert_steps", out var raw) || string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
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

    private static string BuildStringList(IReadOnlyList<string>? items)
    {
        if (items is null || items.Count == 0) return "[]";
        var quoted = items.Select(p => "\"" + Escape(p) + "\"");
        return "[" + string.Join(", ", quoted) + "]";
    }

    private static string BuildDeniedActionsJson(IReadOnlyList<DeniedActionEntry>? denials)
    {
        if (denials is null || denials.Count == 0) return "[]";
        return JsonSerializer.Serialize(denials, _jsonOptions);
    }

    private static string BuildInstructionFilesJson(IReadOnlyList<InstructionFileRecord>? files)
    {
        if (files is null || files.Count == 0) return "[]";
        return JsonSerializer.Serialize(files, _jsonOptions);
    }

    private static string BuildPolicyJson(PolicyRecord? policy)
    {
        if (policy is null) return "null";
        return JsonSerializer.Serialize(policy, _jsonOptions);
    }

    private static TaskArtifactDocument ParseMarkdown(string markdown)
    {
        var sections = markdown.Split("---", 3, StringSplitOptions.None);
        if (sections.Length < 3)
        {
            throw new InvalidOperationException("Task artifact markdown has invalid frontmatter.");
        }

        var frontmatter = ParseFrontmatter(sections[1]);
        var body = sections[2];

        return new TaskArtifactDocument(
            TaskId: frontmatter["task_id"],
            Type: frontmatter["type"],
            Status: frontmatter["status"],
            Agent: frontmatter["agent"],
            StartedAt: DateTimeOffset.Parse(frontmatter["started_at"], CultureInfo.InvariantCulture),
            CompletedAt: ParseOptionalTimestamp(frontmatter, "completed_at"),
            SourceRef: Unquote(frontmatter["source_ref"]),
            PagesTouched: ParseStringList(frontmatter, "pages_touched"),
            FailureReason: ParseOptionalString(frontmatter, "failure_reason"),
            Narrative: NarrativeWithoutUserPromptSection(body),
            PagesCreated: ParseStringList(frontmatter, "pages_created"),
            PagesUpdated: ParseStringList(frontmatter, "pages_updated"),
            PagesSuperseded: ParseStringList(frontmatter, "pages_superseded"),
            DeniedActions: ParseJsonField<List<DeniedActionEntry>>(frontmatter, "denied_actions", "[]"),
            InstructionFiles: ParseJsonField<List<InstructionFileRecord>>(frontmatter, "instruction_files", "[]"),
            Policy: ParseJsonField<PolicyRecord>(frontmatter, "policy", "null"),
            Model: ParseOptionalString(frontmatter, "model"),
            Turns: ParseOptionalInt(frontmatter, "turns"),
            RolledBack: ParseOptionalBool(frontmatter, "rolled_back"),
            UserPromptSource: ParseOptionalString(frontmatter, "user_prompt_source"),
            UserPrompt: ExtractSection(body, "## User Prompt"),
            ConvertSteps: ParseConvertSteps(frontmatter),
            Title: ParseOptionalString(frontmatter, "title"));
    }

    /// <summary>Splits the frontmatter block into its flat `key: value` pairs; a line without a colon is ignored.</summary>
    private static Dictionary<string, string> ParseFrontmatter(string frontmatter)
        => frontmatter
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);

    /// <summary>
    /// Reads a frontmatter key that <see cref="BuildMarkdown"/> writes as the bare literal
    /// <c>null</c> when the value is absent — the shape every optional scalar field below shares.
    /// </summary>
    private static bool TryGetWritten(Dictionary<string, string> fm, string key, out string raw)
        => fm.TryGetValue(key, out raw!) && !string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? ParseOptionalTimestamp(Dictionary<string, string> fm, string key)
        => TryGetWritten(fm, key, out var raw) ? DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture) : null;

    private static int? ParseOptionalInt(Dictionary<string, string> fm, string key)
        => TryGetWritten(fm, key, out var raw) ? int.Parse(raw, CultureInfo.InvariantCulture) : null;

    private static bool? ParseOptionalBool(Dictionary<string, string> fm, string key)
        => TryGetWritten(fm, key, out var raw) ? string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) : null;

    /// <summary>
    /// Reads a frontmatter value stored as inline JSON. <paramref name="emptyLiteral"/> is the
    /// form <see cref="BuildMarkdown"/> writes for "nothing recorded" (<c>[]</c> for the lists,
    /// <c>null</c> for the policy). A crash mid-write can leave the value truncated; a partial
    /// read is treated as absent rather than fatal so the surviving fields still parse.
    /// </summary>
    private static T? ParseJsonField<T>(Dictionary<string, string> fm, string key, string emptyLiteral)
        where T : class
    {
        if (!fm.TryGetValue(key, out var raw) || string.Equals(raw, emptyLiteral, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NarrativeWithoutUserPromptSection(string body)
    {
        var idx = body.IndexOf("## User Prompt", StringComparison.Ordinal);
        return (idx < 0 ? body : body[..idx]).Trim();
    }

    /// <summary>Extracts the content of one `## Heading` body section, up to the next `## ` heading.</summary>
    private static string? ExtractSection(string body, string heading)
    {
        var start = body.IndexOf(heading + "\n", StringComparison.Ordinal);
        if (start < 0)
        {
            start = body.IndexOf(heading + "\r\n", StringComparison.Ordinal);
            if (start < 0)
                return null;
        }

        var contentStart = start + heading.Length;
        var next = body.IndexOf("\n## ", contentStart, StringComparison.Ordinal);
        var section = next < 0 ? body[contentStart..] : body[contentStart..next];
        var trimmed = section.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static IReadOnlyList<string> ParseStringList(Dictionary<string, string> fm, string key)
    {
        if (!fm.TryGetValue(key, out var raw) || raw == "[]")
            return [];
        return raw.Trim('[', ']')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => v.Trim().Trim('"'))
            .ToList();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// The exact inverse of <see cref="Escape"/> plus the surrounding quotes: strips one
    /// enclosing quote pair (never more — a value ending in an escaped quote would otherwise
    /// lose a character) and undoes the <c>\"</c> / <c>\\</c> escaping. 023: titles carry
    /// both characters.
    /// </summary>
    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    /// <summary>Reads a nullable quoted frontmatter string, treating the bare literal <c>null</c> as absent.</summary>
    private static string? ParseOptionalString(Dictionary<string, string> fm, string key)
        => TryGetWritten(fm, key, out var raw) ? Unquote(raw) : null;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}
