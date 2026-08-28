using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// 028-lint-at-scale, US2 (FR-003, FR-004, SC-002) — proves the harness-computed
/// <c>WikiCoverage</c> mechanism: <see cref="GuardedToolExecutor.ConsideredPaths"/>
/// accumulates from real <c>read_file</c>/<c>search_files</c> results (never from a bare
/// <c>list_files</c> result) and, combined with a filesystem page-count snapshot, yields the
/// correct <c>Complete</c>/<c>Partial</c> status — exactly the computation
/// <c>LintIntentHandler.ExecuteAsync</c> performs, exercised here at the harness level since
/// that internal type is not visible across the assembly boundary (mirrors
/// <c>LintAtScaleCompletionTests</c>'s scope).
/// </summary>
public class LintWikiCoverageTests : IDisposable
{
    private readonly string _root;
    private readonly GuardedToolExecutor _executor;

    public LintWikiCoverageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"wiki-coverage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var policy = new SafetyPolicy(_root, readPrefixes: [_root + Path.DirectorySeparatorChar], writePrefixes: []);
        _executor = new GuardedToolExecutor(policy, new WriteJournal(), _root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void SeedPage(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static int CountMarkdownPages(string root)
        => Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories).Count();

    [Fact]
    public async Task CompletePass_ReadingEveryPage_ProducesCompleteStatus_WithPagesConsideredEqualToPagesTotal()
    {
        SeedPage("alpha.md", "# Alpha\n");
        SeedPage("beta.md", "# Beta\n");
        SeedPage("gamma.md", "# Gamma\n");

        var fake = new FakeModelClient([
            FakeModelClient.ReadFileTurn("call-1", "alpha.md"),
            FakeModelClient.ReadFileTurn("call-2", "beta.md"),
            FakeModelClient.ReadFileTurn("call-3", "gamma.md"),
            FakeModelClient.FinalTurn("Wiki health check complete."),
        ]);
        var loop = new AgentLoop(fake, _executor, registry: LintToolRegistry.Default);

        await loop.RunAsync(
            "You are a test lint agent.",
            [new ConversationMessage("user", "Perform the wiki health check now.")],
            "run-coverage-complete",
            CancellationToken.None);

        var pagesTotal = CountMarkdownPages(_root);
        var coverage = WikiCoverage.Compute(pagesTotal, _executor.ConsideredPaths.Count);

        Assert.Equal(3, pagesTotal);
        Assert.Equal(3, coverage.PagesConsidered);
        Assert.Equal(WikiCoverage.StatusComplete, coverage.Status);
    }

    [Fact]
    public async Task ForcedPartialPass_StoppingEarly_ProducesPartialStatus_WithPagesConsideredLessThanPagesTotal()
    {
        SeedPage("alpha.md", "# Alpha\n");
        SeedPage("beta.md", "# Beta\n");
        SeedPage("gamma.md", "# Gamma\n");

        // Only two of the three pages are ever read before the run ends — simulating a
        // run that stopped (budget, turn cap, or the agent's own judgment) before
        // surveying the whole wiki.
        var fake = new FakeModelClient([
            FakeModelClient.ReadFileTurn("call-1", "alpha.md"),
            FakeModelClient.ReadFileTurn("call-2", "beta.md"),
            FakeModelClient.FinalTurn("Wiki health check complete (budget exhausted)."),
        ]);
        var loop = new AgentLoop(fake, _executor, registry: LintToolRegistry.Default);

        await loop.RunAsync(
            "You are a test lint agent.",
            [new ConversationMessage("user", "Perform the wiki health check now.")],
            "run-coverage-partial",
            CancellationToken.None);

        var pagesTotal = CountMarkdownPages(_root);
        var coverage = WikiCoverage.Compute(pagesTotal, _executor.ConsideredPaths.Count);

        Assert.Equal(3, pagesTotal);
        Assert.Equal(2, coverage.PagesConsidered);
        Assert.True(coverage.PagesConsidered < coverage.PagesTotal);
        Assert.Equal(WikiCoverage.StatusPartial, coverage.Status);
    }

    [Fact]
    public async Task PageNamedOnlyInAListFilesResult_IsNeverAddedToConsideredPaths()
    {
        SeedPage("alpha.md", "# Alpha\n");
        SeedPage("beta.md", "# Beta\n");

        // The agent lists the directory (seeing both pages' names) but only ever opens
        // one of them — spec.md Edge Cases: "list_files alone does not count as
        // considering a page" (data-model.md ConsideredPaths invariant).
        var fake = new FakeModelClient([
            FakeModelClient.ListFilesTurn("call-1", "."),
            FakeModelClient.ReadFileTurn("call-2", "alpha.md"),
            FakeModelClient.FinalTurn("Wiki health check complete."),
        ]);
        var loop = new AgentLoop(fake, _executor, registry: LintToolRegistry.Default);

        await loop.RunAsync(
            "You are a test lint agent.",
            [new ConversationMessage("user", "Perform the wiki health check now.")],
            "run-coverage-list-only",
            CancellationToken.None);

        Assert.Equal(1, _executor.ConsideredPaths.Count);
        Assert.DoesNotContain(_executor.ConsideredPaths, p => p.EndsWith("beta.md", StringComparison.Ordinal));
    }
}
