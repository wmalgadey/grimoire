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
            _ => throw new InvalidOperationException($"Unknown Query scorer '{scorerId}'."),
        };

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
}
