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
        var finishedAt = doc.FinishedAt.HasValue ? doc.FinishedAt.Value.ToString("O") : "null";
        var failureFirstLine = string.IsNullOrWhiteSpace(doc.FailureReason) ? doc.FailureReason : doc.FailureReason.Split('\n')[0];
        var failure = string.IsNullOrWhiteSpace(failureFirstLine) ? "null" : $"\"{Escape(failureFirstLine)}\"";
        var createdPaths = BuildStringListYaml(doc.CreatedPaths);
        var updatedPaths = BuildStringListYaml(doc.UpdatedPaths);
        var supersededPaths = BuildStringListYaml(doc.SupersededPaths);
        var deniedActions = BuildDeniedActionsYaml(doc.DeniedActions.Count == 0 ? null : doc.DeniedActions);
        var userQuestions = BuildUserQuestionsYaml(doc.UserQuestions);
        var instructionContext = BuildInstructionContextYaml(doc.InstructionContext);

        return $"---\n" +
               $"task_id: {doc.TaskId}\n" +
               $"operation: {doc.Operation}\n" +
               $"status: {doc.Status}\n" +
               $"started_at: {doc.StartedAt:O}\n" +
               $"finished_at: {finishedAt}\n" +
               $"source_ref: \"{Escape(doc.SourceRef)}\"\n" +
               $"created_paths: {createdPaths}\n" +
               $"updated_paths: {updatedPaths}\n" +
               $"superseded_paths: {supersededPaths}\n" +
               deniedActions +
               userQuestions +
               instructionContext +
               $"failure_reason: {failure}\n" +
               $"---\n\n" +
               doc.Summary.TrimEnd() + "\n";
    }

    private static string BuildStringListYaml(IReadOnlyList<string> values)
    {
        return values.Count == 0
            ? "[]"
            : $"[{string.Join(", ", values.Select(p => $"\"{Escape(p)}\""))}]";
    }

    private static string BuildDeniedActionsYaml(IReadOnlyList<DeniedActionRecord>? deniedActions)
    {
        if (deniedActions is null || deniedActions.Count == 0)
        {
            return "denied_actions: []\n";
        }

        var builder = new StringBuilder();
        builder.AppendLine("denied_actions:");
        foreach (var denied in deniedActions)
        {
            builder.AppendLine($"  - action: \"{Escape(denied.Action)}\"");
            builder.AppendLine($"    target_path: \"{Escape(denied.TargetPath)}\"");
            builder.AppendLine($"    reason: \"{Escape(denied.Reason)}\"");
        }

        return builder.ToString();
    }

    private static string BuildInstructionContextYaml(InstructionContextRecord? instructionContext)
    {
        if (instructionContext is null)
        {
            return string.Empty;
        }

        var skills = instructionContext.SkillPaths.Count == 0
            ? "[]"
            : $"[{string.Join(", ", instructionContext.SkillPaths.Select(s => $"\"{Escape(s)}\""))}]";

        return "instruction_context:\n" +
               $"  claude_path: \"{Escape(instructionContext.ClaudePath)}\"\n" +
               $"  skill_paths: {skills}\n" +
               $"  content_hash: \"{Escape(instructionContext.ContentHash)}\"\n";
    }

    private static string BuildUserQuestionsYaml(IReadOnlyList<string> userQuestions)
    {
        if (userQuestions.Count == 0)
        {
            return "user_questions: []\n";
        }

        var values = string.Join(", ", userQuestions.Select(x => $"\"{Escape(x)}\""));
        return $"user_questions: [{values}]\n";
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

        var createdPaths = frontmatter.TryGetValue("created_paths", out var createdRaw) && createdRaw != "[]"
            ? createdRaw.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote).ToList()
            : new List<string>();

        var updatedPaths = frontmatter.TryGetValue("updated_paths", out var updatedRaw) && updatedRaw != "[]"
            ? updatedRaw.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote).ToList()
            : new List<string>();

        var supersededPaths = frontmatter.TryGetValue("superseded_paths", out var supersededRaw) && supersededRaw != "[]"
            ? supersededRaw.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote).ToList()
            : new List<string>();

        if (createdPaths.Count == 0 && updatedPaths.Count == 0 && frontmatter.TryGetValue("pages_touched", out var pagesRaw) && pagesRaw != "[]")
        {
            updatedPaths = pagesRaw.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote).ToList();
        }

        var deniedActions = ParseDeniedActions(sections[1]);
        var instructionContext = ParseInstructionContext(sections[1]);
        var userQuestions = frontmatter.TryGetValue("user_questions", out var questionsRaw) && questionsRaw != "[]"
            ? questionsRaw.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote).ToList()
            : new List<string>();

        DateTimeOffset? finishedAt = frontmatter.TryGetValue("finished_at", out var finishedAtRaw) && !string.Equals(finishedAtRaw, "null", StringComparison.OrdinalIgnoreCase)
            ? DateTimeOffset.Parse(finishedAtRaw, CultureInfo.InvariantCulture)
            : frontmatter.TryGetValue("completed_at", out var completedAtRaw) && !string.Equals(completedAtRaw, "null", StringComparison.OrdinalIgnoreCase)
            ? DateTimeOffset.Parse(completedAtRaw, CultureInfo.InvariantCulture)
            : null;

        var failureReason = frontmatter.TryGetValue("failure_reason", out var failureRaw) && !string.Equals(failureRaw, "null", StringComparison.OrdinalIgnoreCase)
            ? Unquote(failureRaw)
            : null;

        return new TaskArtifactDocument(
            TaskId: frontmatter["task_id"],
            Operation: frontmatter.TryGetValue("operation", out var operation) ? operation : frontmatter.GetValueOrDefault("type", "ingest"),
            Status: frontmatter["status"],
            StartedAt: DateTimeOffset.Parse(frontmatter["started_at"], CultureInfo.InvariantCulture),
            FinishedAt: finishedAt,
            SourceRef: Unquote(frontmatter["source_ref"]),
            CreatedPaths: createdPaths,
            UpdatedPaths: updatedPaths,
            SupersededPaths: supersededPaths,
            DeniedActions: deniedActions,
            UserQuestions: userQuestions,
            Summary: sections[2].Trim(),
            FailureReason: failureReason,
            InstructionContext: instructionContext);
    }

    private static IReadOnlyList<DeniedActionRecord> ParseDeniedActions(string frontmatterText)
    {
        var lines = frontmatterText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var results = new List<DeniedActionRecord>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Trim().StartsWith("- action:", StringComparison.Ordinal))
            {
                continue;
            }

            var action = UnquoteFromLine(lines[i]);
            var targetPath = i + 1 < lines.Length ? UnquoteFromLine(lines[i + 1]) : string.Empty;
            var reason = i + 2 < lines.Length ? UnquoteFromLine(lines[i + 2]) : string.Empty;
            results.Add(new DeniedActionRecord(action, targetPath, reason));
            i += 2;
        }

        return results;
    }

    private static InstructionContextRecord? ParseInstructionContext(string frontmatterText)
    {
        var lines = frontmatterText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, l => l.Trim().Equals("instruction_context:", StringComparison.Ordinal));
        if (start < 0)
        {
            return null;
        }

        var claudePath = string.Empty;
        var skillPaths = new List<string>();
        var contentHash = string.Empty;

        for (var i = start + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("claude_path:", StringComparison.Ordinal) &&
                !trimmed.StartsWith("skill_paths:", StringComparison.Ordinal) &&
                !trimmed.StartsWith("content_hash:", StringComparison.Ordinal))
            {
                if (!trimmed.StartsWith("#", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("-", StringComparison.Ordinal))
                {
                    break;
                }
            }

            if (trimmed.StartsWith("claude_path:", StringComparison.Ordinal))
            {
                claudePath = trimmed.Split(':', 2)[1].Trim().Trim('"');
                continue;
            }

            if (trimmed.StartsWith("skill_paths:", StringComparison.Ordinal))
            {
                var raw = trimmed.Split(':', 2)[1].Trim();
                if (raw.StartsWith("[", StringComparison.Ordinal) && raw.EndsWith("]", StringComparison.Ordinal))
                {
                    skillPaths = raw.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => s.Trim('"')).ToList();
                }

                continue;
            }

            if (trimmed.StartsWith("content_hash:", StringComparison.Ordinal))
            {
                contentHash = trimmed.Split(':', 2)[1].Trim().Trim('"');
            }
        }

        return string.IsNullOrWhiteSpace(claudePath) && string.IsNullOrWhiteSpace(contentHash)
            ? null
            : new InstructionContextRecord(claudePath, skillPaths, contentHash);
    }

    private static string UnquoteFromLine(string line)
    {
        var value = line.Split(':', 2).ElementAtOrDefault(1)?.Trim() ?? string.Empty;
        return value.Trim('"');
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
