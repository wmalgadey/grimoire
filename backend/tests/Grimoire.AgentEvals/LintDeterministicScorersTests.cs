using Grimoire.EvalRunner.Scoring;

namespace Grimoire.AgentEvals;

/// <summary>
/// T032/T033 (013-lint-agent, US2) sanity coverage for the two new deterministic Lint
/// scorers — pure regression insurance for the scoring mechanism itself (no agent
/// involved, no live capture), same spirit as this project's other hermetic scorer/
/// staleness checks. Structural eval-scenario wiring (fixture, scenario definition,
/// scorer registration) is otherwise unexercised until the Phase 6 capture/replay task
/// (T046/T047), per T017/T018's established deferral.
/// </summary>
public class LintDeterministicScorersTests
{
    [Fact]
    public void MetadataProposals_NarrativeNamingBothPagesWithConformingProposals_Passes()
    {
        const string narrative =
            """
            ## Metadata Hygiene

            ### Missing tags on undertagged-topic

            [[undertagged-topic]] has no tags. Propose `concept/Caching` and `pattern/ReadThrough`.

            ### Missing confidence on unscored-topic

            [[unscored-topic]] has no confidence score. Propose `medium` — one source, internally consistent.
            """;

        var score = LintDeterministicScorers.Score("lint-metadata-proposals", new LintSampleRunData(narrative));

        Assert.True(score.Pass);
        Assert.True(score.Checks["mentions_undertagged_page"]);
        Assert.True(score.Checks["proposes_taxonomy_conforming_tag"]);
        Assert.True(score.Checks["mentions_unscored_page"]);
        Assert.True(score.Checks["proposes_confidence_level"]);
    }

    [Fact]
    public void MetadataProposals_NarrativeMissingConfidenceProposal_Fails()
    {
        const string narrative =
            """
            ## Metadata Hygiene

            ### Missing tags on undertagged-topic

            [[undertagged-topic]] has no tags. Propose `concept/Caching`.
            """;

        var score = LintDeterministicScorers.Score("lint-metadata-proposals", new LintSampleRunData(narrative));

        Assert.False(score.Pass);
        Assert.False(score.Checks["mentions_unscored_page"]);
        Assert.False(score.Checks["proposes_confidence_level"]);
    }

    [Fact]
    public void InboundLinksRefreshed_AgainstTheUnmodifiedStaleFixture_EveryPageFailsAccuracy()
    {
        // The lint-inbound-links-fixture is deliberately seeded with stale counts, per its
        // own page bodies' documented true-count comments — a run that never executed
        // (this test never runs one) must show every page inaccurate.
        var wikiRoot = FindFixtureWikiRoot();

        var score = LintDeterministicScorers.Score(
            "lint-inbound-links-refreshed", new LintSampleRunData(string.Empty, wikiRoot));

        Assert.False(score.Pass);
        Assert.False(score.Checks["hub-page_inbound_links_accurate"]);
        Assert.False(score.Checks["spoke-a_inbound_links_accurate"]);
        Assert.False(score.Checks["spoke-b_inbound_links_accurate"]);
    }

    [Fact]
    public void InboundLinksRefreshed_AfterCorrectingEveryCount_AllPagesPassAccuracy()
    {
        var wikiRoot = CopyFixtureToTempDir();
        try
        {
            SetInboundLinks(Path.Combine(wikiRoot, "pages", "hub-page.md"), 3);
            SetInboundLinks(Path.Combine(wikiRoot, "pages", "spoke-a.md"), 2);
            SetInboundLinks(Path.Combine(wikiRoot, "pages", "spoke-b.md"), 1);

            var score = LintDeterministicScorers.Score(
                "lint-inbound-links-refreshed", new LintSampleRunData(string.Empty, wikiRoot));

            Assert.True(score.Pass);
            Assert.True(score.Checks["hub-page_inbound_links_accurate"]);
            Assert.True(score.Checks["spoke-a_inbound_links_accurate"]);
            Assert.True(score.Checks["spoke-b_inbound_links_accurate"]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(wikiRoot)!, recursive: true);
        }
    }

    [Fact]
    public void InboundLinksRefreshed_OnlyOneCountCorrected_PartialAccuracy_Fails()
    {
        var wikiRoot = CopyFixtureToTempDir();
        try
        {
            SetInboundLinks(Path.Combine(wikiRoot, "pages", "hub-page.md"), 3);
            // spoke-a/spoke-b left stale.

            var score = LintDeterministicScorers.Score(
                "lint-inbound-links-refreshed", new LintSampleRunData(string.Empty, wikiRoot));

            Assert.False(score.Pass);
            Assert.True(score.Checks["hub-page_inbound_links_accurate"]);
            Assert.False(score.Checks["spoke-a_inbound_links_accurate"]);
            Assert.False(score.Checks["spoke-b_inbound_links_accurate"]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(wikiRoot)!, recursive: true);
        }
    }

    private static void SetInboundLinks(string pagePath, int value)
    {
        var content = File.ReadAllText(pagePath);
        var updated = System.Text.RegularExpressions.Regex.Replace(
            content, @"^inbound_links:\s*\d+\s*$", $"inbound_links: {value}", System.Text.RegularExpressions.RegexOptions.Multiline);
        File.WriteAllText(pagePath, updated);
    }

    private static string CopyFixtureToTempDir()
    {
        var source = FindFixtureWikiRoot();
        var dest = Path.Combine(Path.GetTempPath(), $"lint-inbound-links-scorer-test-{Guid.NewGuid():N}", "wiki");
        CopyDirectory(source, dest);
        return dest;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }
    }

    private static string FindFixtureWikiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "tests", "Grimoire.AgentEvals", "Fixtures", "lint-inbound-links-fixture")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate repository root from " + AppContext.BaseDirectory);
        }

        return Path.Combine(dir.FullName, "backend", "tests", "Grimoire.AgentEvals", "Fixtures", "lint-inbound-links-fixture", "wiki");
    }
}
