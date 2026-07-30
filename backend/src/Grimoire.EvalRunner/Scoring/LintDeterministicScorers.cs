namespace Grimoire.EvalRunner.Scoring;

/// <summary>
/// Everything a Lint scorer may inspect about one sample's outcome: the run's final
/// narrative (the Findings Report body an agent-behavior harness would extract from the
/// terminal <c>completed</c> event's <c>summary</c>/the Findings Report file). No
/// structured parser exists for the Findings Report format (contracts/
/// findings-report-format.md's "Parsing" section) — scorers read the raw text, per
/// T017/T018's "lightweight text/wikilink matching ... no structured parser needed".
/// </summary>
public sealed record LintSampleRunData(string Narrative);

/// <summary>
/// The deterministic per-sample checks for the two Lint eval scenarios (013-lint-agent
/// T017/T018, SC-005/SC-006), scored against the <c>lint-seeded-defects</c> fixture
/// (<see cref="Scenarios.LintScenarioDefinitions"/>). These verify agent output against
/// spec success criteria — harness verification, not agent judgment (Constitution
/// Principle V): the judgment of what constitutes a finding stays entirely in
/// <c>agents/lint/system-prompt.md</c> and the recorded model behavior being scored.
/// </summary>
public static class LintDeterministicScorers
{
    public static SampleScore Score(string scorerId, LintSampleRunData run)
        => scorerId switch
        {
            "lint-defects-found" => DefectsFound(run),
            "lint-genuine-findings" => GenuineFindings(run),
            _ => throw new InvalidOperationException($"Unknown Lint scorer '{scorerId}'."),
        };

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

    /// <summary>
    /// SC-006 (≥ 90% of sampled findings are genuine — the described problem exists in
    /// the pages named): a lightweight proxy check, since there is no structured parser
    /// to extract individual findings' affected-page claims. Checks that the narrative
    /// does not merely list page names without any descriptive/remediation text around
    /// them (a report of bare wikilinks with no explanation would be ungrounded, easy to
    /// fabricate, and unhelpful — the genuine-finding signal this scorer can verify
    /// mechanically) and that it does not invent a page name absent from the fixture
    /// entirely (a hallucinated finding about a nonexistent page is never genuine).
    /// </summary>
    private static SampleScore GenuineFindings(LintSampleRunData run)
    {
        var narrative = run.Narrative;

        var hasProposedRemediation = narrative.Contains("**Proposed remediation**", StringComparison.Ordinal);
        var mentionsAtLeastOneKnownPage = KnownFixturePages.Any(page => Mentions(narrative, page));
        var noHallucinatedFixturePage = !LooksLikeHallucinatedPageName(narrative);

        var checks = new Dictionary<string, bool>
        {
            ["has_proposed_remediation"] = hasProposedRemediation,
            ["mentions_at_least_one_known_page"] = mentionsAtLeastOneKnownPage,
            ["no_obviously_hallucinated_page_reference"] = noHallucinatedFixturePage,
        };

        return new SampleScore(
            hasProposedRemediation && mentionsAtLeastOneKnownPage && noHallucinatedFixturePage, false, checks);
    }

    private static readonly string[] KnownFixturePages =
    [
        "cache-invalidation-ttl", "cache-invalidation-events", "retry-backoff", "circuit-breaker",
        "orphan-topic", "undertagged-topic", "unscored-topic", "stale-topic",
    ];

    private static bool Mentions(string narrative, string pageSlug)
        => narrative.Contains(pageSlug, StringComparison.OrdinalIgnoreCase);

    private static bool MentionsAny(string narrative, params string[] pageSlugs)
        => pageSlugs.Any(slug => Mentions(narrative, slug));

    /// <summary>
    /// Heuristic only: a genuinely hallucinated page reference cannot be enumerated in
    /// advance, so this checks for the one mechanically detectable shape — a wikilink
    /// whose slug does not resolve to any fixture page and does not look like one of the
    /// fixture's own topic-folder-free flat slugs. Kept intentionally conservative (few
    /// false positives) since this is a proxy signal, not the actual judgment.
    /// </summary>
    private static bool LooksLikeHallucinatedPageName(string narrative)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(narrative, @"\[\[([a-zA-Z0-9/_-]+)\]\]");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var slug = match.Groups[1].Value.Split('/').Last();
            if (!KnownFixturePages.Contains(slug, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
