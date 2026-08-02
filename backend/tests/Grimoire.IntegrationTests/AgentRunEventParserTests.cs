using Grimoire.Hub.AgentDispatch;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T006 (015-lint-board-parity, ADR-008/ADR-018) — <see cref="AgentRunEventParser"/>
/// tolerance for the two new terminal-event fields (<c>proposedActions</c>,
/// <c>remediationOutcome</c>): pre-015 event lines still parse unchanged, the new fields
/// round-trip, and malformed <c>proposedActions</c> entries are skipped without failing
/// the whole event. Entry-level tolerance is implemented in
/// <see cref="TolerantProposedActionListConverter"/>: ADR-008's line-level tolerance
/// ("a bad line never fails the run") applied one level down — a bad list entry never
/// fails the event; a non-array <c>proposedActions</c> value is treated as absent.
/// </summary>
public class AgentRunEventParserTests
{
    // ------------------------------------------------------------- existing tolerance (unchanged)

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain progress text, not JSON")]
    [InlineData("{not valid json")]
    [InlineData("""{"type":"completed"}""")] // missing taskId
    [InlineData("""{"taskId":"t-1"}""")] // missing type
    [InlineData("""{"type":"exploded","taskId":"t-1","timestamp":"2026-08-01T09:00:00Z"}""")] // unknown type
    public void NonEventLines_YieldNull_NeverThrow(string line)
    {
        Assert.Null(AgentRunEventParser.TryParse(line));
    }

    [Fact]
    public void Pre015CompletedEvent_ParsesUnchanged_WithNullNewFields()
    {
        // A recorded pre-015 terminal line (deniedActions/createdPages vocabulary only):
        // backward compatibility — old fixtures and replays must parse as before.
        const string line =
            """
            {"type":"completed","taskId":"2026-07-30-lint-9f8e7d","timestamp":"2026-07-30T09:04:09Z","summary":"Lint completed.","deniedActions":[{"action":"write_page","requestedTarget":"wiki/x.md","canonicalTarget":"wiki/x.md","reason":"frontmatter-only","turn":3}],"createdPages":[]}
            """;

        var parsed = AgentRunEventParser.TryParse(line);

        Assert.NotNull(parsed);
        Assert.Equal(AgentRunEvent.TypeCompleted, parsed.Type);
        Assert.Equal("Lint completed.", parsed.Summary);
        Assert.Single(parsed.DeniedActions!);
        Assert.Null(parsed.ProposedActions);
        Assert.Null(parsed.RemediationOutcome);
    }

    [Fact]
    public void UnknownFields_AreStillIgnored()
    {
        const string line =
            """
            {"type":"completed","taskId":"t-1","timestamp":"2026-08-01T09:00:00Z","someFutureField":{"nested":true}}
            """;

        Assert.NotNull(AgentRunEventParser.TryParse(line));
    }

    // ------------------------------------------------------------- proposedActions (FR-007)

    [Fact]
    public void ProposedActions_RoundTrip_WithOptionalTargetPath()
    {
        const string line =
            """
            {"type":"completed","taskId":"2026-08-01-lint-9f8e7d","timestamp":"2026-08-01T09:04:09Z","summary":"2 actionable findings.","proposedActions":[{"title":"Add missing tags to runtime-paths page","description":"The page wiki/runtime-paths.md has no tags frontmatter.","targetPath":"wiki/runtime-paths.md"},{"title":"Clarify stale link","description":"index.md links a superseded page."}]}
            """;

        var parsed = AgentRunEventParser.TryParse(line);

        Assert.NotNull(parsed?.ProposedActions);
        Assert.Equal(2, parsed.ProposedActions.Count);
        Assert.Equal(
            new AgentRunEventProposedAction(
                "Add missing tags to runtime-paths page",
                "The page wiki/runtime-paths.md has no tags frontmatter.",
                "wiki/runtime-paths.md"),
            parsed.ProposedActions[0]);
        Assert.Equal(
            new AgentRunEventProposedAction("Clarify stale link", "index.md links a superseded page."),
            parsed.ProposedActions[1]);
        Assert.Null(parsed.ProposedActions[1].TargetPath);
    }

    [Fact]
    public void EmptyProposedActionsList_ParsesAsEmpty_NotAsError()
    {
        // Spec US3 scenario 2: an empty list is a clean run, not an error.
        const string line =
            """
            {"type":"completed","taskId":"t-1","timestamp":"2026-08-01T09:00:00Z","proposedActions":[]}
            """;

        var parsed = AgentRunEventParser.TryParse(line);

        Assert.NotNull(parsed?.ProposedActions);
        Assert.Empty(parsed.ProposedActions);
    }

    [Fact]
    public void MalformedProposedActionEntries_AreSkipped_EventStillParses()
    {
        // Entry-level tolerance: missing description, non-string title, whitespace-only
        // title, and non-object entries are each dropped — the one well-formed entry
        // survives and the terminal event itself is never lost.
        const string line =
            """
            {"type":"completed","taskId":"t-1","timestamp":"2026-08-01T09:00:00Z","proposedActions":[{"title":"only a title"},{"title":42,"description":"numeric title"},{"title":"   ","description":"blank title"},"not an object",17,null,{"title":"Valid entry","description":"The only well-formed proposal.","targetPath":"wiki/a.md"}]}
            """;

        var parsed = AgentRunEventParser.TryParse(line);

        Assert.NotNull(parsed?.ProposedActions);
        var action = Assert.Single(parsed.ProposedActions);
        Assert.Equal(new AgentRunEventProposedAction("Valid entry", "The only well-formed proposal.", "wiki/a.md"), action);
    }

    [Fact]
    public void NonArrayProposedActions_IsTreatedAsAbsent()
    {
        const string line =
            """
            {"type":"completed","taskId":"t-1","timestamp":"2026-08-01T09:00:00Z","proposedActions":"not a list"}
            """;

        var parsed = AgentRunEventParser.TryParse(line);

        Assert.NotNull(parsed);
        Assert.Null(parsed.ProposedActions);
    }

    [Fact]
    public void MalformedTargetPath_DegradesToNull_EntryKept()
    {
        // targetPath is an optional opaque hint (data-model.md): a malformed hint costs
        // the hint, never the proposal.
        const string line =
            """
            {"type":"completed","taskId":"t-1","timestamp":"2026-08-01T09:00:00Z","proposedActions":[{"title":"T","description":"D","targetPath":7}]}
            """;

        var parsed = AgentRunEventParser.TryParse(line);

        var action = Assert.Single(parsed!.ProposedActions!);
        Assert.Equal(new AgentRunEventProposedAction("T", "D"), action);
    }

    // ------------------------------------------------------------- remediationOutcome (FR-018)

    [Fact]
    public void RemediationOutcome_NotApplicable_RoundTrips_WithReason()
    {
        const string line =
            """
            {"type":"completed","taskId":"2026-08-01-remediation-a1b2c3","timestamp":"2026-08-01T09:09:30Z","summary":"Tags already present; proposal is moot.","remediationOutcome":"not_applicable","reason":"The page gained a tags list after this action was proposed."}
            """;

        var parsed = AgentRunEventParser.TryParse(line);

        Assert.NotNull(parsed);
        Assert.Equal(AgentRunEvent.RemediationOutcomeNotApplicable, parsed.RemediationOutcome);
        Assert.Equal("The page gained a tags list after this action was proposed.", parsed.Reason);
    }

    [Fact]
    public void RemediationOutcome_Applied_RoundTrips()
    {
        const string line =
            """
            {"type":"completed","taskId":"2026-08-01-remediation-a1b2c3","timestamp":"2026-08-01T09:09:30Z","remediationOutcome":"applied"}
            """;

        var parsed = AgentRunEventParser.TryParse(line);

        Assert.Equal(AgentRunEvent.RemediationOutcomeApplied, parsed!.RemediationOutcome);
    }

    [Fact]
    public void AbsentRemediationOutcome_DeserializesToNull()
    {
        const string line =
            """
            {"type":"completed","taskId":"t-1","timestamp":"2026-08-01T09:00:00Z"}
            """;

        Assert.Null(AgentRunEventParser.TryParse(line)!.RemediationOutcome);
    }
}
