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

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"task_id: {doc.TaskId}");
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
        sb.AppendLine($"failure_reason: {failure}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(doc.Narrative.TrimEnd());
        sb.AppendLine();
        return sb.ToString();
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

        var frontmatter = sections[1]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);

        static string Unquote(string value) => value.Trim().Trim('"');

        var pagesTouched = ParseStringList(frontmatter, "pages_touched");
        var pagesCreated = ParseStringList(frontmatter, "pages_created");
        var pagesUpdated = ParseStringList(frontmatter, "pages_updated");
        var pagesSuperseded = ParseStringList(frontmatter, "pages_superseded");

        DateTimeOffset? completedAt = frontmatter.TryGetValue("completed_at", out var completedAtRaw) && !string.Equals(completedAtRaw, "null", StringComparison.OrdinalIgnoreCase)
            ? DateTimeOffset.Parse(completedAtRaw, CultureInfo.InvariantCulture)
            : null;

        var failureReason = frontmatter.TryGetValue("failure_reason", out var failureRaw) && !string.Equals(failureRaw, "null", StringComparison.OrdinalIgnoreCase)
            ? Unquote(failureRaw)
            : null;

        var model = frontmatter.TryGetValue("model", out var modelRaw) && !string.Equals(modelRaw, "null", StringComparison.OrdinalIgnoreCase)
            ? Unquote(modelRaw)
            : null;

        int? turns = frontmatter.TryGetValue("turns", out var turnsRaw) && !string.Equals(turnsRaw, "null", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(turnsRaw, CultureInfo.InvariantCulture)
            : null;

        bool? rolledBack = frontmatter.TryGetValue("rolled_back", out var rbRaw) && !string.Equals(rbRaw, "null", StringComparison.OrdinalIgnoreCase)
            ? string.Equals(rbRaw, "true", StringComparison.OrdinalIgnoreCase)
            : null;

        // Denied actions and instruction_files are stored as JSON; parse inline.
        IReadOnlyList<DeniedActionEntry>? deniedActions = null;
        if (frontmatter.TryGetValue("denied_actions", out var daRaw) && !string.Equals(daRaw, "[]", StringComparison.OrdinalIgnoreCase))
        {
            try { deniedActions = JsonSerializer.Deserialize<List<DeniedActionEntry>>(daRaw, _jsonOptions); }
            catch { /* ignore parse errors on partial reads */ }
        }

        IReadOnlyList<InstructionFileRecord>? instructionFiles = null;
        if (frontmatter.TryGetValue("instruction_files", out var ifRaw) && !string.Equals(ifRaw, "[]", StringComparison.OrdinalIgnoreCase))
        {
            try { instructionFiles = JsonSerializer.Deserialize<List<InstructionFileRecord>>(ifRaw, _jsonOptions); }
            catch { /* ignore parse errors on partial reads */ }
        }

        PolicyRecord? policy = null;
        if (frontmatter.TryGetValue("policy", out var policyRaw) && !string.Equals(policyRaw, "null", StringComparison.OrdinalIgnoreCase))
        {
            try { policy = JsonSerializer.Deserialize<PolicyRecord>(policyRaw, _jsonOptions); }
            catch { /* ignore parse errors on partial reads */ }
        }

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
            Narrative: sections[2].Trim(),
            PagesCreated: pagesCreated,
            PagesUpdated: pagesUpdated,
            PagesSuperseded: pagesSuperseded,
            DeniedActions: deniedActions,
            InstructionFiles: instructionFiles,
            Policy: policy,
            Model: model,
            Turns: turns,
            RolledBack: rolledBack);
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

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}
