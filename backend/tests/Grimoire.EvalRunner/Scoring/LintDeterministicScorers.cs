namespace Grimoire.EvalRunner.Scoring;

/// <summary>
/// Everything a Lint scorer may inspect about one sample's outcome: the run's final
/// narrative (the Findings Report body an agent-behavior harness would extract from the
/// terminal <c>completed</c> event's <c>summary</c>/the Findings Report file). No
/// structured parser exists for the Findings Report format (contracts/
/// findings-report-format.md's "Parsing" section) — scorers read the raw text, per
/// T017/T018's "lightweight text/wikilink matching ... no structured parser needed".
/// <paramref name="WikiRoot"/> is the sandbox wiki root a run executed against,
/// mirroring <c>QuerySampleRunData.WikiRoot</c>'s reason for existing (a scorer that
/// must look beyond the narrative at the mutated wiki state).
/// </summary>
public sealed record LintSampleRunData(
    string Narrative,
    string WikiRoot,
    IReadOnlyList<Workspace.RemediationProposalEntry>? ProposedActions = null,
    int? ContentTokensRead = null,
    int? ContextBudgetTokens = null)
{
    /// <summary>Convenience constructor for scorers that only need the narrative (SC-005/SC-006/SC-007).</summary>
    public LintSampleRunData(string narrative) : this(narrative, string.Empty, null)
    {
    }
}

/// <summary>
/// The deterministic per-sample checks for the remaining Lint eval scenario
/// (<see cref="Scenarios.LintScenarioDefinitions.AtScaleSurvey"/>). These verify agent
/// output against spec success criteria — harness verification, not agent judgment
/// (Constitution Principle V): the judgment of what constitutes a finding stays entirely
/// in <c>agents/lint/system-prompt.md</c> and the recorded model behavior being scored.
/// Scorers for the removed lower-stakes scenarios (Constitution Principle II, v1.12.0)
/// were deleted along with their scenarios.
/// </summary>
public static class LintDeterministicScorers
{
    public static SampleScore Score(string scorerId, LintSampleRunData run)
        => scorerId switch
        {
            "lint-at-scale-survey" => AtScaleSurvey(run),
            _ => throw new InvalidOperationException($"Unknown Lint scorer '{scorerId}'."),
        };

    /// <summary>
    /// SC-011 (026-guarded-tool-surface): on a wiki larger than the run's context guard,
    /// the survey completes while the content read stays under that guard.
    ///
    /// <para>Both halves are required, and the reason both are is that either alone is
    /// trivially satisfiable in the wrong direction: an agent that reads nothing stays well
    /// under budget and finds nothing, and an agent that reads the whole wiki finds
    /// everything and blows the budget. The scenario is only passed by narrowing that
    /// preserved the survey.</para>
    ///
    /// <para>The defect half is <see cref="DefectsFound"/> — the <c>lint-at-scale</c>
    /// fixture carries <c>lint-seeded-defects</c>' pages verbatim, so "still a real survey"
    /// means finding those seeded defects. The budget half compares the page content
    /// the run actually read (<see cref="Recording.ReadShapeAccounting"/>) against the
    /// scenario's declared budget — SC-011's own quantity, and the one
    /// <c>specs/026-guarded-tool-surface/baseline.md</c> measures, so the eval and the
    /// before/after record are denominated the same way.</para>
    /// </summary>
    private static SampleScore AtScaleSurvey(LintSampleRunData run)
    {
        if (run.ContextBudgetTokens is not { } budget)
        {
            throw new InvalidOperationException(
                "Scorer 'lint-at-scale-survey' requires the scenario's ContextBudgetTokens (SC-011).");
        }

        if (run.ContentTokensRead is not { } contentRead)
        {
            throw new InvalidOperationException(
                "Scorer 'lint-at-scale-survey' requires the sample's ContentTokensRead (SC-011).");
        }

        var defects = DefectsFound(run);
        var withinBudget = contentRead <= budget;

        var checks = new Dictionary<string, bool>(defects.Checks ?? new Dictionary<string, bool>())
        {
            ["survey_still_finds_defects"] = defects.Pass,
            ["stayed_within_context_budget"] = withinBudget,
        };

        return new SampleScore(defects.Pass && withinBudget, false, checks);
    }

    /// <summary>
    /// SC-005 (≥ 85% of seeded defects found, per category): checks that each of the six
    /// seeded defects' affected page(s) — named by wikilink or bare slug — are mentioned
    /// anywhere in the narrative. A named check per defect (not just an aggregate Pass)
    /// so a threshold run's per-category recall is visible in the eval summary, matching
    /// T017's "per category" framing even though the underlying aggregation (fraction of
    /// samples with Pass == true) is sample-level, mirroring every other scorer's shape
    /// in this file (Constitution II: the harness verifies output, not judgment).
    /// </summary>
    private static SampleScore DefectsFound(LintSampleRunData run)
    {
        var narrative = run.Narrative;

        var contradictionFound = MentionsAny(narrative, "cache-invalidation-ttl", "cache-invalidation-events");
        var missingCrossReferenceFound = MentionsAny(narrative, "retry-backoff", "circuit-breaker");
        var orphanFound = Mentions(narrative, "orphan-topic");
        var missingTagsFound = Mentions(narrative, "undertagged-topic");
        var missingConfidenceFound = Mentions(narrative, "unscored-topic");
        var staleLowConfidenceFound = Mentions(narrative, "stale-topic");

        var checks = new Dictionary<string, bool>
        {
            ["contradiction_found"] = contradictionFound,
            ["missing_cross_reference_found"] = missingCrossReferenceFound,
            ["orphan_found"] = orphanFound,
            ["missing_tags_found"] = missingTagsFound,
            ["missing_confidence_found"] = missingConfidenceFound,
            ["stale_low_confidence_found"] = staleLowConfidenceFound,
        };

        var foundCount = checks.Values.Count(v => v);
        // SC-005's ≥85% threshold is applied at the eval-summary level (success rate
        // across samples); a single sample's Pass requires finding at least 5 of the 6
        // seeded defects (≈83%, the nearest attainable fraction below the 85% target for
        // a fixture of exactly 6) — one missed defect in an otherwise-thorough run is not
        // a scenario failure, six missed would be.
        var pass = foundCount >= 5;

        return new SampleScore(pass, false, checks);
    }

    private static bool Mentions(string narrative, string pageSlug)
        => narrative.Contains(pageSlug, StringComparison.OrdinalIgnoreCase);

    private static bool MentionsAny(string narrative, params string[] pageSlugs)
        => pageSlugs.Any(slug => Mentions(narrative, slug));
}
