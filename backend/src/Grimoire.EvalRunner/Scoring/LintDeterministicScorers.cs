namespace Grimoire.EvalRunner.Scoring;

/// <summary>
/// Everything a Lint scorer may inspect about one sample's outcome: the run's final
/// narrative (the Findings Report body an agent-behavior harness would extract from the
/// terminal <c>completed</c> event's <c>summary</c>/the Findings Report file). No
/// structured parser exists for the Findings Report format (contracts/
/// findings-report-format.md's "Parsing" section) — scorers read the raw text, per
/// T017/T018's "lightweight text/wikilink matching ... no structured parser needed".
/// <paramref name="WikiRoot"/> is the sandbox wiki root a run executed against — T033's
/// inbound-link scorer needs it to recompute the true link graph and inspect each page's
/// post-run frontmatter, mirroring <c>QuerySampleRunData.WikiRoot</c>'s reason for
/// existing (a scorer that must look beyond the narrative at the mutated wiki state).
/// </summary>
public sealed record LintSampleRunData(string Narrative, string WikiRoot)
{
    /// <summary>Convenience constructor for scorers that only need the narrative (SC-005/SC-006/SC-007).</summary>
    public LintSampleRunData(string narrative) : this(narrative, string.Empty)
    {
    }
}

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
            "lint-metadata-proposals" => MetadataProposals(run),
            "lint-inbound-links-refreshed" => InboundLinksRefreshed(run),
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
    /// T032 (SC-007, ≥ 90% tag-taxonomy conformance / ≥ 90% confidence-convention
    /// conformance): a proxy check over the narrative text, since — like
    /// <see cref="GenuineFindings"/> — no structured parser extracts an individual
    /// proposal's tag/confidence value. Checks that the narrative both names each seeded
    /// metadata-hygiene defect page (<c>undertagged-topic</c>/<c>unscored-topic</c>, from
    /// <see cref="Scenarios.LintScenarioDefinitions.SeededDefectsFixtureName"/>) and, near
    /// it, proposes something shaped like a real tag-taxonomy entry
    /// (<c>agents/ingest/system-prompt.md</c>'s namespaced prefixes) or a real confidence
    /// level (<c>high</c>/<c>medium</c>/<c>low</c>) — the taxonomy/formula's own content
    /// is never re-implemented here (Constitution Principle V): this only recognizes the
    /// convention's shape in already-agent-authored text.
    /// </summary>
    private static SampleScore MetadataProposals(LintSampleRunData run)
    {
        var narrative = run.Narrative;

        var mentionsUndertaggedPage = Mentions(narrative, "undertagged-topic");
        var proposesTaxonomyConformingTag = TagTaxonomyPrefixes.Any(
            prefix => narrative.Contains(prefix, StringComparison.OrdinalIgnoreCase));

        var mentionsUnscoredPage = Mentions(narrative, "unscored-topic");
        var proposesConfidenceLevel = ConfidenceLevels.Any(level => System.Text.RegularExpressions.Regex.IsMatch(
            narrative, $@"\b{level}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        var checks = new Dictionary<string, bool>
        {
            ["mentions_undertagged_page"] = mentionsUndertaggedPage,
            ["proposes_taxonomy_conforming_tag"] = proposesTaxonomyConformingTag,
            ["mentions_unscored_page"] = mentionsUnscoredPage,
            ["proposes_confidence_level"] = proposesConfidenceLevel,
        };

        var pass = mentionsUndertaggedPage && proposesTaxonomyConformingTag
            && mentionsUnscoredPage && proposesConfidenceLevel;

        return new SampleScore(pass, false, checks);
    }

    private static readonly string[] TagTaxonomyPrefixes =
        ["person/", "company/", "tech/", "pattern/", "concept/", "source-type/"];

    private static readonly string[] ConfidenceLevels = ["high", "medium", "low"];

    /// <summary>
    /// T033 (SC-008, ≥ 95% accurate inbound-link counts): a fully mechanical recomputation
    /// — never a judgment about wiki content (Constitution Principle V) — of the true
    /// <c>[[wikilink]]</c> graph across every post-run page plus <c>index.md</c>/
    /// <c>log.md</c>, compared against each page's own recorded <c>inbound_links</c>
    /// frontmatter value. Requires <see cref="LintSampleRunData.WikiRoot"/> (the sandbox
    /// the sampled run actually executed against) — unscoreable without it.
    /// </summary>
    private static SampleScore InboundLinksRefreshed(LintSampleRunData run)
    {
        if (string.IsNullOrEmpty(run.WikiRoot) || !Directory.Exists(run.WikiRoot))
        {
            return new SampleScore(false, false, new Dictionary<string, bool> { ["wiki_root_available"] = false });
        }

        var pagesDir = Path.Combine(run.WikiRoot, "pages");
        var pageFiles = Directory.Exists(pagesDir)
            ? Directory.GetFiles(pagesDir, "*.md", SearchOption.AllDirectories)
            : [];

        var sources = new List<string>(pageFiles);
        foreach (var sideFile in new[] { "index.md", "log.md" })
        {
            var sidePath = Path.Combine(run.WikiRoot, sideFile);
            if (File.Exists(sidePath))
            {
                sources.Add(sidePath);
            }
        }

        var trueInboundCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            var sourceSlug = Path.GetFileNameWithoutExtension(source);
            var content = File.ReadAllText(source);
            foreach (System.Text.RegularExpressions.Match match in WikilinkPattern.Matches(content))
            {
                var targetSlug = match.Groups[1].Value.Split('/')[^1];
                if (string.Equals(targetSlug, sourceSlug, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // A page never counts its own self-reference (system-prompt.md: "other pages").
                }

                trueInboundCounts[targetSlug] = trueInboundCounts.GetValueOrDefault(targetSlug) + 1;
            }
        }

        var checks = new Dictionary<string, bool>();
        foreach (var pageFile in pageFiles)
        {
            var slug = Path.GetFileNameWithoutExtension(pageFile);
            var content = File.ReadAllText(pageFile);
            var recordedMatch = InboundLinksFieldPattern.Match(content);
            var recorded = recordedMatch.Success ? int.Parse(recordedMatch.Groups[1].Value) : (int?)null;
            var expected = trueInboundCounts.GetValueOrDefault(slug);

            checks[$"{slug}_inbound_links_accurate"] = recorded == expected;
        }

        var pass = checks.Count > 0 && checks.Values.All(v => v);
        return new SampleScore(pass, false, checks);
    }

    private static readonly System.Text.RegularExpressions.Regex WikilinkPattern =
        new(@"\[\[([a-zA-Z0-9/_-]+)\]\]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex InboundLinksFieldPattern =
        new(@"^inbound_links:\s*(\d+)\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);

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
