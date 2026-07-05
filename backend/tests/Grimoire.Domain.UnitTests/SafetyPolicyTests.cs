using Grimoire.Domain.Guardrails;

namespace Grimoire.Domain.UnitTests;

public class SafetyPolicyTests
{
    private const string RepoRoot = "/repo";

    private static SafetyPolicy BuildPolicy(
        string[]? readPrefixes = null,
        string[]? writePrefixes = null)
        => new(
            repositoryRoot: RepoRoot,
            readPrefixes: readPrefixes ?? [],
            writePrefixes: writePrefixes ?? []);

    // ── Deny-by-default ──────────────────────────────────────────────────────────

    [Fact]
    public void EmptyReadRules_DeniesReadRequest()
    {
        var policy = BuildPolicy(readPrefixes: []);

        var decision = policy.Evaluate("/repo/wiki/pages/foo.md", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("no_rule", decision.DenialReason);
    }

    [Fact]
    public void EmptyWriteRules_DeniesWriteRequest()
    {
        var policy = BuildPolicy(writePrefixes: []);

        var decision = policy.Evaluate("/repo/wiki/pages/foo.md", isWrite: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal("out_of_scope", decision.DenialReason);
    }

    // ── Prefix matching ───────────────────────────────────────────────────────────

    [Fact]
    public void ReadPrefix_AllowsMatchingPath()
    {
        var policy = BuildPolicy(readPrefixes: ["/repo/wiki/"]);

        var decision = policy.Evaluate("/repo/wiki/pages/foo.md", isWrite: false);

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.DenialReason);
    }

    [Fact]
    public void ReadPrefix_DeniesNonMatchingPath()
    {
        var policy = BuildPolicy(readPrefixes: ["/repo/wiki/"]);

        var decision = policy.Evaluate("/repo/agents/ingest/CLAUDE.md", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("no_rule", decision.DenialReason);
    }

    [Fact]
    public void ExactFilePrefix_AllowsThatExactFile()
    {
        var policy = BuildPolicy(readPrefixes: ["/repo/wiki/index.md"]);

        var decision = policy.Evaluate("/repo/wiki/index.md", isWrite: false);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void ExactFilePrefix_DeniesOtherFilesInSameDirectory()
    {
        var policy = BuildPolicy(readPrefixes: ["/repo/wiki/index.md"]);

        var decision = policy.Evaluate("/repo/wiki/log.md", isWrite: false);

        Assert.False(decision.IsAllowed);
    }

    // ── Read/write scope separation ───────────────────────────────────────────────

    [Fact]
    public void ReadPrefix_DoesNotGrantWrite()
    {
        var policy = BuildPolicy(
            readPrefixes: ["/repo/wiki/"],
            writePrefixes: []);

        var decision = policy.Evaluate("/repo/wiki/pages/foo.md", isWrite: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal("out_of_scope", decision.DenialReason);
    }

    [Fact]
    public void WritePrefix_DoesNotGrantRead()
    {
        var policy = BuildPolicy(
            readPrefixes: [],
            writePrefixes: ["/repo/wiki/pages/"]);

        var decision = policy.Evaluate("/repo/wiki/pages/foo.md", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("no_rule", decision.DenialReason);
    }

    // ── Traversal escape ──────────────────────────────────────────────────────────

    [Fact]
    public void PathOutsideRepoRoot_DeniedWithTraversalReason_ForRead()
    {
        var policy = BuildPolicy(readPrefixes: ["/repo/wiki/"]);

        // Path is already canonical but escapes the repo root.
        var decision = policy.Evaluate("/etc/passwd", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("traversal", decision.DenialReason);
    }

    [Fact]
    public void PathOutsideRepoRoot_DeniedWithTraversalReason_ForWrite()
    {
        var policy = BuildPolicy(writePrefixes: ["/repo/wiki/pages/"]);

        var decision = policy.Evaluate("/tmp/evil.md", isWrite: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal("traversal", decision.DenialReason);
    }

    [Fact]
    public void PathJustAboveRepoRoot_DeniedWithTraversalReason()
    {
        // A canonical path that starts with repo root prefix but resolves above it.
        var policy = BuildPolicy(readPrefixes: ["/repo/"]);

        var decision = policy.Evaluate("/rep", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("traversal", decision.DenialReason);
    }

    [Fact]
    public void SiblingPathWithSharedPrefix_DeniedWithTraversalReason()
    {
        var policy = BuildPolicy(readPrefixes: ["/repo/wiki/"]);

        var decision = policy.Evaluate("/repo2/wiki/index.md", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("traversal", decision.DenialReason);
    }
}
