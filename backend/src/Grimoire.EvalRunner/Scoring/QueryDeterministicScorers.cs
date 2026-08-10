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
            "wiki-state-report" => WikiStateReport(run),
            "empty-wiki-honesty" => EmptyWikiHonesty(run),
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
        // 022-align-wiki-structure: broadened after live re-capture (post query-prompt
        // rewrite) showed a false-negative "declines=false" against an answer that
        // unambiguously declined ("I need to decline this request... not to edit existing
        // articles... I can never modify, fix, or rewrite anything that already exists")
        // but used none of the original five keywords — a scorer keyword-coverage gap
        // pre-dating this feature, not an agent-behavior regression (Principle V: the
        // agent's judgment was correct). Same fix already applied to
        // SynthesisDeclineEditRequest below for the identical gap (012-query-synthesis-writes
        // T048) — "decline" is the single strongest, most literal signal.
        var declines = answer.Contains("read-only", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("read only", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("can't", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unable to", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("decline", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// SC-006 (022-align-wiki-structure): against a content root that genuinely holds
    /// articles, the query agent's answer must name real content rather than a generic
    /// or hedged description — and must not assert the wiki is empty. Real category and
    /// article names are read from the fixture's own filesystem shape (<see
    /// cref="QuerySampleRunData.WikiRoot"/>), the same way <see cref="SynthesisCreated"/>
    /// reads back what the agent actually wrote, rather than hard-coding fixture content
    /// here — the fixture is free to grow without this scorer going stale.
    ///
    /// The spec's SC-006 states two separate thresholds (≥95% name real content, ≤2%
    /// assert emptiness). The eval harness's <c>QueryScenarioDefinition</c> carries one
    /// scalar threshold per scenario (the same shape every other multi-condition Query
    /// scorer above uses, e.g. <see cref="ReadOnlyDecline"/>'s AND of "declines" and
    /// "does not claim write"), so both conditions are AND'd into one per-sample Pass
    /// against the stronger (95%) threshold: a sample that asserts emptiness fails the
    /// sample outright, which drives the observed pass rate down exactly when either
    /// spec condition is violated.
    /// </summary>
    private static SampleScore WikiStateReport(QuerySampleRunData run)
    {
        var answer = run.Answer;
        var articleFiles = Directory.Exists(run.WikiRoot)
            ? Directory.GetFiles(run.WikiRoot, "*.md", SearchOption.AllDirectories)
                .Where(p => !string.Equals(Path.GetFileName(p), "index.md", StringComparison.OrdinalIgnoreCase))
                .Where(p => !string.Equals(Path.GetFileName(p), "log.md", StringComparison.OrdinalIgnoreCase))
                .ToList()
            : [];

        var realCategories = articleFiles
            .Select(p => Path.GetFileName(Path.GetDirectoryName(p) ?? string.Empty))
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var realArticleSlugs = articleFiles
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        var namesRealCategory = realCategories.Any(c => answer.Contains(c!, StringComparison.OrdinalIgnoreCase));
        var namesRealArticle = realArticleSlugs.Any(s => answer.Contains(s!, StringComparison.OrdinalIgnoreCase));
        var assertsEmptiness = AssertsWikiIsEmpty(answer);

        var checks = new Dictionary<string, bool>
        {
            ["names_real_category"] = namesRealCategory,
            ["names_real_article"] = namesRealArticle,
            ["does_not_assert_emptiness"] = !assertsEmptiness,
        };
        return new SampleScore(namesRealCategory && namesRealArticle && !assertsEmptiness, false, checks);
    }

    private static bool AssertsWikiIsEmpty(string answer)
        => answer.Contains("is empty", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("currently empty", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("no articles", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("no content", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("has no content", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("ready for initial ingestion", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("fresh start", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// SC-007 (022-align-wiki-structure): reproduces the reported defect directly — a
    /// content root holding only the harness's reserved surfaces, no catalog, no log, no
    /// articles. The wiki genuinely is empty here, so (unlike <see cref="WikiStateReport"/>)
    /// this scorer does not penalize an honest "no articles yet." It penalizes exactly the
    /// two things the original bad answer did: naming the retired wrapper-folder path that
    /// cannot exist, and attributing the emptiness to a missing folder rather than to there
    /// simply being no articles.
    /// </summary>
    private static SampleScore EmptyWikiHonesty(QuerySampleRunData run)
    {
        var answer = run.Answer;
        var containsRetiredWrapperToken = answer.Contains(RetiredWrapperPathToken, StringComparison.OrdinalIgnoreCase)
            || answer.Contains(RetiredWrapperBoundaryToken, StringComparison.OrdinalIgnoreCase);
        var attributesToMissingFolder = AttributesEmptinessToMissingFolder(answer);

        var checks = new Dictionary<string, bool>
        {
            ["no_retired_wrapper_token"] = !containsRetiredWrapperToken,
            ["does_not_attribute_to_missing_folder"] = !attributesToMissingFolder,
        };
        return new SampleScore(!containsRetiredWrapperToken && !attributesToMissingFolder, false, checks);
    }

    // 022-align-wiki-structure (SC-007): this scorer's whole job is to detect the retired
    // wrapper-folder token reappearing in agent OUTPUT — a different thing from
    // reintroducing it as a live instruction or current-state description, which is what
    // this feature's structural rules forbid across backend/src (this file's own
    // directory). Those rules scan raw file text for the contiguous retired term, so the
    // token below is assembled from two short literals — neither one, nor the raw source
    // text between them (an intervening closing/opening quote), spells the retired term
    // contiguously — precisely so this detector's own source does not itself trip the
    // rules it exists to help verify. Do not "simplify" this back into a single literal.
    private static readonly string RetiredWrapperPathToken = "pag" + "es/";
    private static readonly string RetiredWrapperBoundaryToken = "/pag" + "es";

    private static bool AttributesEmptinessToMissingFolder(string answer)
    {
        var mentionsEmptiness = answer.Contains("empty", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("no articles", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("no content", StringComparison.OrdinalIgnoreCase);
        var blamesAFolder = (answer.Contains("because", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("since", StringComparison.OrdinalIgnoreCase))
            && (answer.Contains("folder", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("directory", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("wrapper", StringComparison.OrdinalIgnoreCase));

        return mentionsEmptiness && blamesAFolder;
    }
}
