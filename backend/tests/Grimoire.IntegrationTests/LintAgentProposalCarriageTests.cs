using Grimoire.AgentRuntime.RunEvents;
using Grimoire.Hub.AgentDispatch;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T025 (015-lint-board-parity, US3) — the Lint agent's mechanical proposal carriage
/// (Constitution Principle V: loop mechanics only, content agent-authored):
/// <see cref="ProposedActionsBlock"/> lifts the fenced <c>proposed-actions</c> block off
/// the final narrative verbatim, and the entries survive the full transport —
/// <see cref="RunEventEmitter"/> NDJSON terminal event → Hub-side
/// <see cref="AgentRunEventParser"/> — unchanged.
/// </summary>
public class LintAgentProposalCarriageTests
{
    private const string NarrativeWithBlock =
        """
        ## Content Quality

        ### Missing cross-reference between [[a]] and [[b]]

        Both pages describe the same flow.

        **Proposed remediation**: link them.

        ## Metadata Hygiene

        No metadata-hygiene findings.

        ## Structure

        No structure findings.

        ```proposed-actions
        [
          {
            "title": "Cross-reference [[a]] and [[b]]",
            "description": "Both pages describe the same flow but do not link each other.\nAdd a See-also wikilink in each body.",
            "targetPath": "pages/a.md"
          },
          {
            "title": "Add missing tags to [[b]]",
            "description": "pages/b.md has no tags frontmatter."
          }
        ]
        ```
        """;

    [Fact]
    public void Extract_LiftsEntriesVerbatim_AndStripsTheBlockFromTheNarrative()
    {
        var (narrative, actions) = ProposedActionsBlock.Extract(NarrativeWithBlock);

        Assert.Equal(2, actions.Count);
        Assert.Equal("Cross-reference [[a]] and [[b]]", actions[0].Title);
        Assert.Equal(
            "Both pages describe the same flow but do not link each other.\nAdd a See-also wikilink in each body.",
            actions[0].Description);
        Assert.Equal("pages/a.md", actions[0].TargetPath);
        Assert.Null(actions[1].TargetPath);

        // The report narrative keeps every finding but loses the transport block.
        Assert.Contains("### Missing cross-reference", narrative, StringComparison.Ordinal);
        Assert.Contains("No structure findings.", narrative, StringComparison.Ordinal);
        Assert.DoesNotContain("proposed-actions", narrative, StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_WithoutBlock_ReturnsNarrativeUnchanged_AndNoActions()
    {
        const string narrative = "## Content Quality\n\nNo content-quality findings.\n";

        var (result, actions) = ProposedActionsBlock.Extract(narrative);

        Assert.Equal(narrative, result);
        Assert.Empty(actions);
    }

    [Fact]
    public void Extract_EmptyArray_YieldsNoActions()
    {
        var (_, actions) = ProposedActionsBlock.Extract("Report.\n\n```proposed-actions\n[]\n```\n");

        Assert.Empty(actions);
    }

    [Fact]
    public void Extract_UnparseableJson_YieldsNoActions_AndKeepsTheNarrativeIntact()
    {
        const string narrative = "Report.\n\n```proposed-actions\nnot json at all\n```\n";

        var (result, actions) = ProposedActionsBlock.Extract(narrative);

        // The malformed block stays visible in the Findings Report, never silently dropped.
        Assert.Equal(narrative, result);
        Assert.Empty(actions);
    }

    [Fact]
    public void Extract_SkipsEntriesWithoutTitleOrDescription_MirroringTheHubParser()
    {
        const string narrative =
            "Report.\n\n```proposed-actions\n" +
            "[{\"title\": \"ok\", \"description\": \"fine\"}, {\"title\": \"no description\"}, \"not an object\"]\n" +
            "```\n";

        var (_, actions) = ProposedActionsBlock.Extract(narrative);

        var action = Assert.Single(actions);
        Assert.Equal("ok", action.Title);
    }

    [Fact]
    public void EmittedTerminalEvent_RoundTripsProposals_ThroughTheHubParser()
    {
        using var writer = new StringWriter();
        using (var emitter = new RunEventEmitter(writer, "2026-08-01-lint-carriage"))
        {
            emitter.EmitCompleted("## Content Quality\n\nNo findings.\n", new RunCompletionMetadata(
                ProposedActions:
                [
                    new ProposedActionRecord(
                        "Cross-reference [[a]] and [[b]]",
                        "Both pages describe the same flow.",
                        "pages/a.md"),
                    new ProposedActionRecord("Add missing tags to [[b]]", "pages/b.md has no tags frontmatter."),
                ]));
        }

        var line = writer.ToString().Trim();
        var parsed = AgentRunEventParser.TryParse(line);

        Assert.NotNull(parsed);
        Assert.Equal(AgentRunEvent.TypeCompleted, parsed!.Type);
        Assert.NotNull(parsed.ProposedActions);
        Assert.Equal(2, parsed.ProposedActions!.Count);
        Assert.Equal("Cross-reference [[a]] and [[b]]", parsed.ProposedActions[0].Title);
        Assert.Equal("Both pages describe the same flow.", parsed.ProposedActions[0].Description);
        Assert.Equal("pages/a.md", parsed.ProposedActions[0].TargetPath);
        Assert.Null(parsed.ProposedActions[1].TargetPath);
    }

    [Fact]
    public void EmittedTerminalEvent_WithoutProposals_OmitsNothingElse()
    {
        using var writer = new StringWriter();
        using (var emitter = new RunEventEmitter(writer, "2026-08-01-lint-carriage"))
        {
            emitter.EmitCompleted("Done.");
        }

        var parsed = AgentRunEventParser.TryParse(writer.ToString().Trim());

        Assert.NotNull(parsed);
        Assert.Null(parsed!.ProposedActions);
        Assert.Equal("Done.", parsed.Summary);
    }
}
