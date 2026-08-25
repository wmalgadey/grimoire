namespace Grimoire.EvalRunner.Scoring;

/// <summary>Everything a Query scorer may inspect about one turn's outcome. Originally
/// (008-query-agent) this was just the answer text, since Query never wrote at all (R3).
/// Since ADR-015 (012-query-synthesis-writes) Query can create Synthesis Pages, so a
/// scorer may also need the wiki-root-relative paths of pages the final turn created and
/// the sandbox wiki root to read their content from — mirrors Ingest's
/// <see cref="SampleRunData"/> (page files, index content) for the same reason.</summary>
public sealed record QuerySampleRunData(
    string Answer,
    IReadOnlyList<string> CreatedPages,
    string WikiRoot)
{
    /// <summary>Convenience constructor for scorers that never inspect writes.</summary>
    public QuerySampleRunData(string answer) : this(answer, [], string.Empty)
    {
    }

    /// <summary>Absolute paths of <see cref="CreatedPages"/>, resolved against <see cref="WikiRoot"/>.</summary>
    public IReadOnlyList<string> CreatedPageFiles => [.. CreatedPages.Select(p => Path.Combine(WikiRoot, p))];
}

/// <summary>
/// The deterministic per-sample checks for the remaining Query eval scenarios — these
/// verify agent output against spec success criteria (harness verification, not agent
/// judgment, Constitution Principle V); the judgment stays in the recorded model behavior
/// being scored. Scorers for the removed lower-stakes scenarios (Constitution Principle
/// II, v1.12.0) were deleted along with their scenarios.
/// </summary>
public static class QueryDeterministicScorers
{
    public static SampleScore Score(string scorerId, QuerySampleRunData run)
        => scorerId switch
        {
            "query-read-only-decline" => ReadOnlyDecline(run),
            "query-synthesis-decline-edit-request" => SynthesisDeclineEditRequest(run),
            _ => throw new InvalidOperationException($"Unknown Query scorer '{scorerId}'."),
        };

    private static SampleScore ReadOnlyDecline(QuerySampleRunData run)
    {
        var answer = run.Answer;
        var declines = answer.Contains("read-only", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("read only", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("can't", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unable to", StringComparison.OrdinalIgnoreCase);
        var claimsToHaveWritten = answer.Contains("I've updated", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I have updated", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I've fixed", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I have fixed", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I've added", StringComparison.OrdinalIgnoreCase);

        var checks = new Dictionary<string, bool>
        {
            ["declines"] = declines,
            ["does_not_claim_write"] = !claimsToHaveWritten,
        };
        return new SampleScore(declines && !claimsToHaveWritten, false, checks);
    }

    /// <summary>
    /// SC-008 (012-query-synthesis-writes): a prompt directly asking the agent to edit
    /// existing wiki content must receive an answer that declines and explains the
    /// boundary — independent of SC-001's structural guarantee (T030) that the edit cannot
    /// happen regardless. Checks both that the answer declines and that it explains *why*
    /// (querying creates new Synthesis Pages rather than editing existing content), plus
    /// the harness-verifiable facts that it does not falsely claim to have edited anything
    /// and did not create a page as a workaround for the declined edit.
    /// </summary>
    private static SampleScore SynthesisDeclineEditRequest(QuerySampleRunData run)
    {
        var answer = run.Answer;
        // T048 (012-query-synthesis-writes): broadened after live capture showed 3/10
        // query-synthesis-decline-edit-request samples scoring a false-negative
        // "declines=false" despite unambiguously declining prose ("I need to decline
        // this request", "I don't modify or edit existing wiki pages", "I'm not able to
        // edit existing wiki pages") — a scorer keyword-coverage gap, not an agent
        // behavior defect (Principle V: the agent's judgment was correct; the harness's
        // textual check of that judgment's observable output was too narrow). Added
        // "not able to", "decline" (the single strongest, most literal signal — every
        // sample used it or an equally explicit cannot/can't), and a stem-tolerant "never
        // modif"/"never edit" in place of the exact-inflection-only "never modifies".
        var declines = answer.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("can't", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unable to", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("not able to", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("does not edit", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("doesn't edit", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("never modif", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("never edit", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("won't edit", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("will not edit", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("decline", StringComparison.OrdinalIgnoreCase);
        var explainsBoundary = answer.Contains("existing", StringComparison.OrdinalIgnoreCase)
            && (answer.Contains("new page", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("create", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("ingest", StringComparison.OrdinalIgnoreCase));
        var claimsToHaveEdited = answer.Contains("I've fixed", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I have fixed", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I've updated", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I have updated", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I've corrected", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I have corrected", StringComparison.OrdinalIgnoreCase);
        var noPageCreatedAsWorkaround = run.CreatedPages.Count == 0;

        var checks = new Dictionary<string, bool>
        {
            ["declines"] = declines,
            ["explains_boundary"] = explainsBoundary,
            ["does_not_claim_edit"] = !claimsToHaveEdited,
            ["no_page_created_as_workaround"] = noPageCreatedAsWorkaround,
        };
        return new SampleScore(
            declines && explainsBoundary && !claimsToHaveEdited && noPageCreatedAsWorkaround,
            false,
            checks);
    }
}
