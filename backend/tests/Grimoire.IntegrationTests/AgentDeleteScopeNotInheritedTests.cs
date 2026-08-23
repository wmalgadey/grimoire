using Grimoire.AgentRuntime.Instructions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T010 (026-guarded-tool-surface, FR-021, research.md D6, ADR-031 R3): the standing
/// regression guard against the finding that shaped this feature's delete-scope design —
/// Ingest's and Query's shipped policies already declare <c>read-write</c> on the content
/// root, so had deletion been evaluated as a write rather than as its own scope, both would
/// have silently gained the ability to delete every page in the wiki as a side effect of a
/// feature about Lint. Loads each agent's real, shipped <c>policy.json</c> through
/// <see cref="PolicyLoader"/> — the same "real policy file" idiom
/// <c>LintInboundLinkRefreshTests</c> uses — and asserts <c>EvaluateDelete</c> denies a path
/// each agent's own write scope otherwise permits, with reason <c>no_rule</c> (no delete
/// rule at all, not merely an out-of-scope target).
/// </summary>
public class AgentDeleteScopeNotInheritedTests
{
    [Fact]
    public async Task IngestPolicy_DeclaresNoDeleteScope_EvaluateDeleteDeniesWithNoRule()
    {
        await AssertNoDeleteScopeAsync("Grimoire.IngestAgent");
    }

    [Fact]
    public async Task QueryPolicy_DeclaresNoDeleteScope_EvaluateDeleteDeniesWithNoRule()
    {
        await AssertNoDeleteScopeAsync("Grimoire.QueryAgent");
    }

    private static async Task AssertNoDeleteScopeAsync(string agentProjectName)
    {
        var root = Path.Combine(Path.GetTempPath(), $"delete-scope-not-inherited-{Guid.NewGuid():N}");
        var wikiRoot = Path.Combine(root, "wiki");
        Directory.CreateDirectory(Path.Combine(wikiRoot, "tech"));

        try
        {
            var repoRoot = FindRepositoryRoot();
            var policyPath = Path.Combine(repoRoot, "backend", "src", agentProjectName, "Instructions", "policy.json");
            Assert.True(File.Exists(policyPath), $"Expected repo file not found: {policyPath}");

            var loader = new PolicyLoader(wikiRoot);
            var loadResult = await loader.LoadAsync(policyPath, CancellationToken.None);
            Assert.True(loadResult.IsFirst(out var loadedPolicy));

            // A path this agent's own write scope permits (content root, not index.md/log.md) —
            // proves the denial is delete-scope-specific, not merely a path the agent cannot
            // reach at all.
            var writablePath = Path.Combine(wikiRoot, "tech", "existing-page.md");
            var writeDecision = loadedPolicy.Policy.Evaluate(writablePath, isWrite: true);
            Assert.True(writeDecision.IsAllowed, $"Expected {agentProjectName}'s policy to permit writing {writablePath}.");

            var deleteDecision = loadedPolicy.Policy.EvaluateDelete(writablePath);
            Assert.False(deleteDecision.IsAllowed);
            Assert.Equal("no_rule", deleteDecision.DenialReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "src", "Grimoire.LintAgent", "Instructions")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root from " + AppContext.BaseDirectory);
    }
}
