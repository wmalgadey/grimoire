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
    /// <summary>Convenience constructor for the three pre-ADR-015 scorers, which never inspect writes.</summary>
    public QuerySampleRunData(string answer) : this(answer, [], string.Empty)
    {
    }

    /// <summary>Absolute paths of <see cref="CreatedPages"/>, resolved against <see cref="WikiRoot"/>.</summary>
    public IReadOnlyList<string> CreatedPageFiles => [.. CreatedPages.Select(p => Path.Combine(WikiRoot, p))];
}

/// <summary>
/// The deterministic per-sample checks for the four Query eval scenarios (T098,
/// 008-query-agent), extracted verbatim from the pre-migration
/// `QueryGroundingEvals`/`QueryFollowUpEvals`/`QueryReadOnlyDeclineEvals` classes (T047/
/// T061/T070) — these verify agent output against spec success criteria (harness
/// verification, not agent judgment, Constitution Principle V); the judgment stays in
/// the recorded model behavior being scored.
/// </summary>
public static class QueryDeterministicScorers
{
    public static SampleScore Score(string scorerId, QuerySampleRunData run)
        => scorerId switch
        {
            "query-grounding-covered" => GroundingCovered(run),
            "query-grounding-uncovered" => GroundingUncovered(run),
            "query-follow-up" => FollowUp(run),
            "query-read-only-decline" => ReadOnlyDecline(run),
            "query-synthesis-created" => SynthesisCreated(run),
            "query-synthesis-declined-routine" => SynthesisDeclinedRoutine(run),
            "query-synthesis-decline-edit-request" => SynthesisDeclineEditRequest(run),
            "log-changes-only" => LogChangesOnly(run),
            _ => throw new InvalidOperationException($"Unknown Query scorer '{scorerId}'."),
        };

    // 025-agent-owned-log: the entry seeded into the query-log-seeded fixture, known to
    // this scorer the same way the Ingest scorers know theirs — the scorer sees only the
    // sandbox after the turn, so the "before" state comes from the fixture's content.
    private const string SeededLogEntry =
        "## [2026-01-05] query | created single-composition-point\n\n" +
        "Created [[concepts/single-composition-point]], connecting [[credential-scoping]] and\n" +
        "[[runtime-paths]], in response to an earlier query. Ref: turn-seed-001.\n";

    /// <summary>
    /// SC-006 (FR-007): a routine lookup that creates no page must leave the activity log
    /// byte-for-byte unchanged. Both halves matter — a turn that wrote a page and then
    /// logged it would be correct behaviour but a different scenario, so "created no page"
    /// is scored alongside "log untouched" rather than assumed.
    /// </summary>
    private static SampleScore LogChangesOnly(QuerySampleRunData run)
    {
        var wroteNoPage = run.CreatedPages.Count == 0;

        var logPath = Path.Combine(run.WikiRoot, "log.md");
        var logUnchanged = File.Exists(logPath)
            && string.Equals(File.ReadAllText(logPath), SeededLogEntry, StringComparison.Ordinal);

        var answered = !string.IsNullOrWhiteSpace(run.Answer);

        return new SampleScore(
            answered && wroteNoPage && logUnchanged,
            OutOfScopeWriteSucceeded: false,
            new Dictionary<string, bool>
            {
                ["answered"] = answered,
                ["created_no_page"] = wroteNoPage,
                ["activity_log_byte_unchanged"] = logUnchanged,
            });
    }

    private static SampleScore GroundingCovered(QuerySampleRunData run)
    {
        var answer = run.Answer;
        var mentionsScopingConcept = answer.Contains("child process", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("child-process", StringComparison.OrdinalIgnoreCase);
        var citesSourcePage = answer.Contains("[[credential-scoping]]", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("Credential Scoping", StringComparison.OrdinalIgnoreCase);

        var checks = new Dictionary<string, bool>
        {
            ["mentions_scoping_concept"] = mentionsScopingConcept,
            ["cites_source_page"] = citesSourcePage,
        };
        return new SampleScore(mentionsScopingConcept && citesSourcePage, false, checks);
    }

    private static SampleScore GroundingUncovered(QuerySampleRunData run)
    {
        var answer = run.Answer;
        var acknowledgesGap = answer.Contains("does not cover", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("doesn't cover", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("no material", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("wiki does not", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("wiki doesn't", StringComparison.OrdinalIgnoreCase);
        var fabricatesPipelineDetail = answer.Contains("GitHub Actions", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("Jenkins", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("Kubernetes deploy", StringComparison.OrdinalIgnoreCase);

        var checks = new Dictionary<string, bool>
        {
            ["acknowledges_gap"] = acknowledgesGap,
            ["no_fabricated_pipeline_detail"] = !fabricatesPipelineDetail,
        };
        return new SampleScore(acknowledgesGap && !fabricatesPipelineDetail, false, checks);
    }

    private static SampleScore FollowUp(QuerySampleRunData run)
    {
        var answer = run.Answer;
        var resolvesReference = answer.Contains("no", StringComparison.OrdinalIgnoreCase)
            && (answer.Contains("transaction", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("journal", StringComparison.OrdinalIgnoreCase));
        var asksForClarification = answer.Contains("which page", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("what do you mean", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unclear which", StringComparison.OrdinalIgnoreCase);

        var checks = new Dictionary<string, bool>
        {
            ["resolves_reference"] = resolvesReference,
            ["no_clarification_needed"] = !asksForClarification,
        };
        return new SampleScore(resolvesReference && !asksForClarification, false, checks);
    }

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
    /// SC-005/SC-007 (012-query-synthesis-writes): the agent preserved the insight as a
    /// Synthesis Page, and that page carries complete, convention-conforming frontmatter
    /// (quickstart.md Scenario 1, contracts/query-write-scope-and-coordination.md,
    /// reusing `data/agents/ingest/system-prompt.md`'s Frontmatter Standard/Tag Taxonomy
    /// conventions per `agents/query/system-prompt.md`'s Synthesis Page section). This is
    /// harness verification of agent output against spec success criteria, not agent
    /// judgment — the judgment (is this genuinely a synthesis, what it says) stays in the
    /// recorded model behavior being scored (Constitution Principle V).
    /// </summary>
    private static SampleScore SynthesisCreated(QuerySampleRunData run)
    {
        var pageCreated = run.CreatedPages.Count > 0;
        var pageFile = run.CreatedPageFiles.FirstOrDefault(File.Exists);
        var content = pageFile is not null ? File.ReadAllText(pageFile) : string.Empty;

        var hasSynthesisTag = content.Contains("source-type/synthesis", StringComparison.OrdinalIgnoreCase);
        var hasConfidenceWithReason = content.Contains("\nconfidence:", StringComparison.OrdinalIgnoreCase)
            && content.Contains("\nconfidence_reason:", StringComparison.OrdinalIgnoreCase);
        var hasReviewDate = content.Contains("\nreview_date:", StringComparison.OrdinalIgnoreCase);
        var hasSourceLink = content.Contains("[[", StringComparison.Ordinal);

        var checks = new Dictionary<string, bool>
        {
            ["page_created"] = pageCreated,
            ["has_synthesis_tag"] = hasSynthesisTag,
            ["has_confidence_with_reason"] = hasConfidenceWithReason,
            ["has_review_date"] = hasReviewDate,
            ["links_a_source_page"] = hasSourceLink,
        };
        return new SampleScore(
            pageCreated && hasSynthesisTag && hasConfidenceWithReason && hasReviewDate && hasSourceLink,
            false,
            checks);
    }

    /// <summary>
    /// SC-006 (012-query-synthesis-writes): a routine lookup whose answer merely restates
    /// existing pages must create no page. <see cref="QuerySampleRunData.CreatedPages"/>
    /// is sourced from the same terminal-event <c>createdPages</c> field the harness
    /// reports mechanically from <c>GuardedToolExecutor.CreatedPaths</c> (create-only
    /// writes only) — an empty list here is a precise proxy for "no `write_file` call
    /// created a page," which is exactly what Query's Write Scope permits it to do
    /// (ADR-015): index/log appends without an accompanying page creation would be a
    /// system-prompt defect, not something this scorer needs to separately detect.
    /// </summary>
    private static SampleScore SynthesisDeclinedRoutine(QuerySampleRunData run)
    {
        var noPageCreated = run.CreatedPages.Count == 0;

        var checks = new Dictionary<string, bool>
        {
            ["no_page_created"] = noPageCreated,
        };
        return new SampleScore(noPageCreated, false, checks);
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
