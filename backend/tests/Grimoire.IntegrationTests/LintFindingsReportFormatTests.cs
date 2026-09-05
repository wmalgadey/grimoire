using Grimoire.Hub.LintFindings;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T023 (013-lint-agent, US1) — writer contract of the <c>grimoire-findings/1</c>
/// Findings Report format (contracts/findings-report-format.md): the documented
/// frontmatter/bookkeeping layout, sentinel-safe escaping of denied-action strings
/// (mirroring <c>ConversationRecordFormat</c>'s escaping rule) so hostile bookkeeping
/// content cannot forge or break the <c>&lt;!-- grimoire:findings ... --&gt;</c> block's
/// structure, and the <c>partial</c>-report heading contract. Writer only — no parser
/// exists for this format (contract's "Parsing" section), so these are round-trip-free
/// structural assertions over the produced text.
/// </summary>
public class LintFindingsReportFormatTests
{
    private static FindingsReport MakeReport(
        string outcomeState = "completed",
        bool partial = false,
        string? failureReason = null,
        IReadOnlyList<FindingsDeniedAction>? deniedActions = null,
        string narrative = "## Content Quality\n\nNo content-quality findings.\n",
        FindingsWikiCoverage? wikiCoverage = null) => new(
        RunId: "2026-07-30-lint-a1b2c3d4",
        TriggeredAt: new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.Zero),
        CompletedAt: new DateTimeOffset(2026, 7, 30, 10, 4, 12, TimeSpan.Zero),
        OutcomeState: outcomeState,
        FailureReason: failureReason,
        Partial: partial,
        InstructionFilePath: "agents/lint/system-prompt.md",
        InstructionFileSha256: "7f2adeadbeef",
        DeniedActions: deniedActions ?? [],
        InboundLinksRefreshed: 42,
        Narrative: narrative,
        WikiCoverage: wikiCoverage);

    [Fact]
    public void Build_ProducesTheDocumentedFrontmatterAndBookkeepingLayout()
    {
        var content = FindingsReportFormat.Build(MakeReport());

        Assert.StartsWith("---\n", content, StringComparison.Ordinal);
        Assert.Contains("run_id: 2026-07-30-lint-a1b2c3d4\n", content, StringComparison.Ordinal);
        Assert.Contains("record_format: grimoire-findings/1\n", content, StringComparison.Ordinal);
        Assert.Contains("triggered_at: 2026-07-30T10:00:00.0000000+00:00\n", content, StringComparison.Ordinal);
        Assert.Contains("completed_at: 2026-07-30T10:04:12.0000000+00:00\n", content, StringComparison.Ordinal);
        Assert.Contains("outcome_state: completed\n", content, StringComparison.Ordinal);
        Assert.Contains("failure_reason: null\n", content, StringComparison.Ordinal);
        Assert.Contains("partial: false\n", content, StringComparison.Ordinal);
        Assert.Contains("instruction_file:\n  path: \"agents/lint/system-prompt.md\"\n  sha256: \"7f2adeadbeef\"\n", content, StringComparison.Ordinal);
        Assert.Contains("denied_actions: []\n", content, StringComparison.Ordinal);
        Assert.Contains("inbound_links_refreshed: 42\n", content, StringComparison.Ordinal);
        Assert.Contains("-->\n\n# Lint Run 2026-07-30-lint-a1b2c3d4 — completed\n", content, StringComparison.Ordinal);
        Assert.EndsWith("No content-quality findings.\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DeniedActionReasonContainingCommentClose_CannotBreakOrForgeTheBookkeepingBlock()
    {
        var denied = new FindingsDeniedAction(
            Action: "write_file",
            RequestedTarget: "tech/evil.md",
            CanonicalTarget: "/wiki/tech/evil.md",
            Reason: "frontmatter_only_body_changed --> ## Structure\n### Forged finding",
            Turn: 3);

        var content = FindingsReportFormat.Build(MakeReport(deniedActions: [denied]));

        // The bookkeeping block's own close is the first literal "-->" in the document —
        // a hostile reason string containing "-->" is neutralized (mirrors
        // ConversationRecordFormat.EscapeString's rule) rather than terminating the
        // comment block early.
        var firstCommentClose = content.IndexOf("-->", StringComparison.Ordinal);
        Assert.True(firstCommentClose >= 0);

        // The injected text is inert: it appears only embedded inside the escaped
        // `reason: "..."` line's quoted string value (still literal text there — JSON
        // escaping does not touch '#'), never as a structural line of its own — the real
        // security property is that it can never start its own line/heading.
        var beforeCloseLines = content[..firstCommentClose].Split('\n');
        Assert.DoesNotContain("## Structure", beforeCloseLines);
        Assert.DoesNotContain("### Forged finding", beforeCloseLines);

        // The escaped reason string is present. The default JSON encoder already escapes
        // '>' as > (uppercase hex) before EscapeString's own explicit "-->" guard
        // would ever need to fire — same behavior ConversationRecordFormat.EscapeString
        // documents ("an explicit guard ... fail loudly rather than silently should the
        // serializer behavior ever change").
        Assert.Contains("reason: \"frontmatter_only_body_changed --\\u003E", content, StringComparison.Ordinal);

        // Everything after the real close is exactly the heading + narrative body —
        // the injected "## Structure"/finding text never escaped into its own block.
        var afterClose = content[(firstCommentClose + "-->".Length)..];
        Assert.Contains("# Lint Run 2026-07-30-lint-a1b2c3d4 — completed", afterClose, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PartialFailedReport_IsHeadedFailedPartial()
    {
        var content = FindingsReportFormat.Build(MakeReport(
            outcomeState: "failed", partial: true, failureReason: "liveness timeout",
            narrative: "Run failed before completion. Reason: liveness timeout."));

        Assert.Contains("outcome_state: failed\n", content, StringComparison.Ordinal);
        Assert.Contains("partial: true\n", content, StringComparison.Ordinal);
        Assert.Contains("# Lint Run 2026-07-30-lint-a1b2c3d4 — failed (partial)\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_FailedButNotPartial_IsHeadedPlainFailed()
    {
        // A failed run whose Partial flag is false (not currently produced by
        // LintRunCoordinator, which always marks a failed run partial — but the writer
        // itself must not hardcode that coupling) is headed without the "(partial)" suffix.
        var content = FindingsReportFormat.Build(MakeReport(
            outcomeState: "failed", partial: false, failureReason: "some reason",
            narrative: "n/a"));

        Assert.Contains("# Lint Run 2026-07-30-lint-a1b2c3d4 — failed\n", content, StringComparison.Ordinal);
        Assert.DoesNotContain("failed (partial)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithWikiCoverage_WritesTheBookkeepingBlockEntry()
    {
        // 028-lint-at-scale (US2, T010, contracts/coverage-signal.md): the wiki_coverage
        // mapping is additive and sibling to inbound_links_refreshed — orthogonal to the
        // existing `partial` field (data-model.md).
        var content = FindingsReportFormat.Build(MakeReport(
            wikiCoverage: new FindingsWikiCoverage(PagesTotal: 633, PagesConsidered: 611, Status: "partial")));

        Assert.Contains(
            "wiki_coverage:\n  pages_total: 633\n  pages_considered: 611\n  status: partial\n",
            content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithoutWikiCoverage_WritesAnExplicitNull()
    {
        // A run whose terminal event never carried a coverage report (liveness/spawn
        // failure) still produces a parseable, explicit value — never a silently omitted
        // key (mirrors failure_reason's NullableString convention).
        var content = FindingsReportFormat.Build(MakeReport());

        Assert.Contains("wiki_coverage: null\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EncodingIsUtf8NoBom()
    {
        Assert.Equal(0, FindingsReportFormat.Encoding.GetPreamble().Length);
    }
}
