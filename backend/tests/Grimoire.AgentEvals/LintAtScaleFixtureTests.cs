using System.Security.Cryptography;
using System.Text;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;

namespace Grimoire.AgentEvals;

/// <summary>
/// T005 (026-guarded-tool-surface, SC-011) — the generated <c>lint-at-scale</c> fixture.
/// These assert the two properties the generator exists to provide, both of which are
/// Grimoire's own contract rather than any library's: the output is byte-deterministic (it
/// feeds a recording staleness fingerprint, so a generator that drifted would invalidate
/// every recording it backs), and it is genuinely larger than the scenario's context budget
/// (without which SC-011 would be measuring nothing).
/// </summary>
public class LintAtScaleFixtureTests
{
    [Fact]
    public void Ensure_IsDeterministic_AcrossRepeatedGeneration()
    {
        var paths = EvalPaths.Discover();

        LintAtScaleFixture.Ensure(paths.FixturesRoot);
        var first = HashTree(paths.FixtureWikiRoot(LintAtScaleFixture.FixtureName));

        // Force a full regeneration rather than letting the stamp short-circuit it: the
        // claim under test is that the bytes are reproducible, not that they are cached.
        Directory.Delete(Path.Combine(paths.FixturesRoot, LintAtScaleFixture.FixtureName), recursive: true);
        LintAtScaleFixture.Ensure(paths.FixturesRoot);
        var second = HashTree(paths.FixtureWikiRoot(LintAtScaleFixture.FixtureName));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Ensure_PreservesEverySeededDefectPage()
    {
        var paths = EvalPaths.Discover();
        LintAtScaleFixture.Ensure(paths.FixturesRoot);

        var source = paths.FixtureWikiRoot(LintAtScaleFixture.SourceFixtureName);
        var generated = paths.FixtureWikiRoot(LintAtScaleFixture.FixtureName);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var copied = Path.Combine(generated, relative);
            Assert.True(File.Exists(copied), $"Seeded fixture file '{relative}' is missing from the generated fixture.");

            // index.md is deliberately extended with the filler catalog; every other seeded
            // page must survive byte-for-byte, or the seeded defects are no longer the
            // defects the shared scorer looks for.
            if (!string.Equals(relative, "index.md", StringComparison.Ordinal))
            {
                Assert.Equal(File.ReadAllText(file), File.ReadAllText(copied));
            }
        }

        // The orphan defect is an absence — a page under the root that nothing links to.
        // It only survives the copy if the page itself does.
        Assert.True(File.Exists(Path.Combine(generated, "orphan-topic.md")));
    }

    [Fact]
    public void Ensure_ProducesAWikiLargerThanTheScenarioContextBudget()
    {
        var paths = EvalPaths.Discover();
        LintAtScaleFixture.Ensure(paths.FixturesRoot);
        var generated = paths.FixtureWikiRoot(LintAtScaleFixture.FixtureName);

        var characters = Directory.EnumerateFiles(generated, "*", SearchOption.AllDirectories)
            .Sum(f => (long)File.ReadAllText(f).Length);

        // ~4 characters per token is the usual rough conversion; the scorer measures real
        // provider token counts, so this only has to establish the inequality with room to
        // spare, not to be exact.
        var approximateTokens = characters / 4;
        var budget = LintScenarioDefinitions.AtScaleSurvey.ContextBudgetTokens
            ?? throw new InvalidOperationException("The at-scale scenario must declare a context budget.");

        Assert.True(
            approximateTokens > budget * 2,
            $"Reading the whole generated wiki (~{approximateTokens} tokens) must cost well over the "
            + $"scenario's {budget}-token budget, or SC-011 tests nothing. Raise "
            + $"{nameof(LintAtScaleFixture)}.{nameof(LintAtScaleFixture.FillerPageCount)}.");
    }

    private static string HashTree(string root)
    {
        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            builder.Append(Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Append('|')
                .Append(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))))
                .Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
