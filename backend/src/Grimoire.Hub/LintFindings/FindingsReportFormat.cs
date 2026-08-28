using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Grimoire.Hub.LintFindings;

/// <summary>One denied tool action, recorded on the Findings Report's bookkeeping block (same shape as every other agent's denials).</summary>
public sealed record FindingsDeniedAction(string Action, string RequestedTarget, string CanonicalTarget, string Reason, int Turn);

/// <summary>
/// 028-lint-at-scale (US2, FR-003/FR-004, contracts/coverage-signal.md): the persisted
/// counterpart of the run's harness-computed coverage report. Orthogonal to
/// <see cref="FindingsReport.Partial"/> (data-model.md) — a completed run can report
/// <see cref="Status"/> <c>"partial"</c> while <c>Partial</c> is <c>false</c>.
/// </summary>
public sealed record FindingsWikiCoverage(int PagesTotal, int PagesConsidered, string Status);

/// <summary>
/// Everything the Hub needs to write one Findings Report file (data-model.md "Findings
/// Report", contracts/findings-report-format.md). Written exactly once, at the run's
/// terminal transition — a Findings Report has exactly one "turn": the run itself.
/// </summary>
public sealed record FindingsReport(
    string RunId,
    DateTimeOffset TriggeredAt,
    DateTimeOffset CompletedAt,
    string OutcomeState,
    string? FailureReason,
    bool Partial,
    string? InstructionFilePath,
    string? InstructionFileSha256,
    IReadOnlyList<FindingsDeniedAction> DeniedActions,
    int InboundLinksRefreshed,
    string Narrative,
    // 028-lint-at-scale (US2, FR-003): null only for a run that never reached a terminal
    // `completed` event with a coverage report attached (a liveness-failed or
    // spawn-failed run) — every completed run's report carries one (SC-002).
    FindingsWikiCoverage? WikiCoverage = null);

/// <summary>
/// Writer for the <c>grimoire-findings/1</c> Findings Report format
/// (contracts/findings-report-format.md). Bodies are agent-authored prose — the same
/// prompt-injection-adjacent surface as the Conversation Record — so this adopts
/// <c>ConversationRecordFormat</c>'s sentinel-safety discipline for the bookkeeping
/// block's string values, even though (per the contract's "Parsing" section) no
/// production code parses a Findings Report back into structured data today: writer only.
/// </summary>
public static class FindingsReportFormat
{
    public const string RecordFormatVersion = "grimoire-findings/1";
    private const string CommentClose = "-->";

    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Encoding used for report files (UTF-8, no BOM — matches every other Hub-written record).</summary>
    public static Encoding Encoding => _utf8NoBom;

    /// <summary>
    /// The complete, self-contained report document: frontmatter, the bookkeeping
    /// comment (run-level facts, JSON-escaped with <c>--&gt;</c> neutralized), and the
    /// agent-authored narrative body verbatim.
    /// </summary>
    public static string Build(FindingsReport report)
    {
        var sb = new StringBuilder();

        sb.Append("---\n");
        sb.Append("run_id: ").Append(report.RunId).Append('\n');
        sb.Append("record_format: ").Append(RecordFormatVersion).Append('\n');
        sb.Append("triggered_at: ").Append(report.TriggeredAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("completed_at: ").Append(report.CompletedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("outcome_state: ").Append(report.OutcomeState).Append('\n');
        sb.Append("failure_reason: ").Append(NullableString(report.FailureReason)).Append('\n');
        sb.Append("partial: ").Append(report.Partial ? "true" : "false").Append('\n');
        sb.Append("instruction_file:\n");
        sb.Append("  path: ").Append(NullableString(report.InstructionFilePath)).Append('\n');
        sb.Append("  sha256: ").Append(NullableString(report.InstructionFileSha256)).Append('\n');

        if (report.DeniedActions.Count == 0)
        {
            sb.Append("denied_actions: []\n");
        }
        else
        {
            sb.Append("denied_actions:\n");
            foreach (var denial in report.DeniedActions)
            {
                sb.Append("  - action: ").Append(EscapeString(denial.Action)).Append('\n');
                sb.Append("    requested_target: ").Append(EscapeString(denial.RequestedTarget)).Append('\n');
                sb.Append("    canonical_target: ").Append(EscapeString(denial.CanonicalTarget)).Append('\n');
                sb.Append("    reason: ").Append(EscapeString(denial.Reason)).Append('\n');
                sb.Append("    turn: ").Append(denial.Turn).Append('\n');
            }
        }

        sb.Append("inbound_links_refreshed: ").Append(report.InboundLinksRefreshed).Append('\n');

        if (report.WikiCoverage is { } coverage)
        {
            sb.Append("wiki_coverage:\n");
            sb.Append("  pages_total: ").Append(coverage.PagesTotal).Append('\n');
            sb.Append("  pages_considered: ").Append(coverage.PagesConsidered).Append('\n');
            sb.Append("  status: ").Append(coverage.Status).Append('\n');
        }
        else
        {
            sb.Append("wiki_coverage: null\n");
        }

        sb.Append(CommentClose).Append('\n');
        sb.Append('\n');

        var headingSuffix = report.OutcomeState == "completed"
            ? "completed"
            : report.Partial ? "failed (partial)" : "failed";
        sb.Append("# Lint Run ").Append(report.RunId).Append(" — ").Append(headingSuffix).Append('\n');
        sb.Append('\n');
        sb.Append(report.Narrative.TrimEnd()).Append('\n');

        return sb.ToString();
    }

    private static string NullableString(string? value) => value is null ? "null" : EscapeString(value);

    /// <summary>
    /// Double-quoted JSON-escaped string with the same explicit <c>--&gt;</c> guard as
    /// <c>ConversationRecordFormat.EscapeString</c> — belt-and-suspenders should the
    /// serializer's own <c>&gt;</c> escaping (which already prevents a literal
    /// <c>--&gt;</c>) ever change behavior.
    /// </summary>
    private static string EscapeString(string value)
    {
        var json = JsonSerializer.Serialize(value);
        if (json.Contains(CommentClose, StringComparison.Ordinal))
        {
            json = json.Replace(CommentClose, "--\\u003e", StringComparison.Ordinal);
        }

        return json;
    }
}
