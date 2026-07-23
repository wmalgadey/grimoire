using System.Text;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.Hub.QueryRunArtifact;

/// <summary>
/// Writes the Query Run Artifact markdown file on a turn's terminal transition
/// (data-model.md Query Run Artifact, ADR-011 R3/R7) — entirely Hub-written; the Query
/// agent process has no write capability at all, so this is the only writer for this
/// record type (unlike Ingest, where the agent process writes its own Task Artifact).
/// </summary>
public sealed class QueryRunArtifactWriter
{
    public async Task WriteAsync(ResolvedGrimoirePaths paths, QueryTurnState turn, CancellationToken cancellationToken = default)
    {
        var filePath = paths.QueryRunArtifactPathFor(turn.ConversationId, turn.TurnId);
        var directory = Path.GetDirectoryName(filePath) ?? ".";
        Directory.CreateDirectory(directory);

        var content = BuildMarkdown(turn);

        // Atomic write (temp + rename), same rationale as HubTaskArtifactWriter: readers
        // must never observe a half-written file.
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

    private static string BuildMarkdown(QueryTurnState turn)
    {
        var metadata = turn.CompletionMetadata;
        var completedAt = turn.CompletedAt.HasValue ? turn.CompletedAt.Value.ToString("O") : "null";
        var failure = turn.FailureReason is null ? "null" : $"\"{Escape(turn.FailureReason)}\"";

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"turn_id: {turn.TurnId}");
        sb.AppendLine($"conversation_id: {turn.ConversationId}");
        sb.AppendLine($"position: {turn.Position}");
        sb.AppendLine($"state: {turn.Status.ToString().ToLowerInvariant()}");
        sb.AppendLine($"started_at: {turn.StartedAt:O}");
        sb.AppendLine($"completed_at: {completedAt}");
        sb.AppendLine($"failure_reason: {failure}");
        sb.AppendLine($"model: {(metadata?.Model is null ? "null" : metadata.Model)}");
        sb.AppendLine($"turns_used: {(metadata?.TurnsUsed?.ToString() ?? "null")}");
        sb.AppendLine("instruction_file:");
        sb.AppendLine($"  path: {(metadata?.SystemPromptSha256 is null ? "null" : "\"agents/query/system-prompt.md\"")}");
        sb.AppendLine($"  sha256: {(metadata?.SystemPromptSha256 is null ? "null" : $"\"{metadata.SystemPromptSha256}\"")}");
        sb.AppendLine("policy:");
        sb.AppendLine($"  path: {(metadata?.PolicyPath is null ? "null" : $"\"{Escape(metadata.PolicyPath)}\"")}");
        sb.AppendLine($"  version: {(metadata?.PolicyVersion?.ToString() ?? "null")}");
        sb.AppendLine($"  sha256: {(metadata?.PolicySha256 is null ? "null" : $"\"{metadata.PolicySha256}\"")}");

        var deniedActions = metadata?.DeniedActions ?? [];
        if (deniedActions.Count == 0)
        {
            sb.AppendLine("denied_actions: []");
        }
        else
        {
            sb.AppendLine("denied_actions:");
            foreach (var denial in deniedActions)
            {
                sb.AppendLine($"  - action: {denial.Action}");
                sb.AppendLine($"    requested_target: \"{Escape(denial.RequestedTarget)}\"");
                sb.AppendLine($"    canonical_target: \"{Escape(denial.CanonicalTarget)}\"");
                sb.AppendLine($"    reason: \"{Escape(denial.Reason)}\"");
                sb.AppendLine($"    turn: {denial.Turn}");
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Prompt");
        sb.AppendLine();
        sb.AppendLine(turn.Prompt.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("## Answer");
        sb.AppendLine();
        sb.Append(turn.Answer.TrimEnd());
        sb.AppendLine();

        return sb.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
