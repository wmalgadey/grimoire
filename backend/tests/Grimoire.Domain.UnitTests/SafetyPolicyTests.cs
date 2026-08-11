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

        var decision = policy.Evaluate("/repo/wiki/tech/foo.md", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("no_rule", decision.DenialReason);
    }

    [Fact]
    public void EmptyWriteRules_DeniesWriteRequest()
    {
        var policy = BuildPolicy(writePrefixes: []);

        var decision = policy.Evaluate("/repo/wiki/tech/foo.md", isWrite: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal("out_of_scope", decision.DenialReason);
    }

    // ── WithNoWriteAccess (015-lint-board-parity T042, ADR-018 message-turn mode) ──

    [Fact]
    public void WithNoWriteAccess_DeniesEveryWrite_EvenWhereTheOriginalPolicyAllowedIt()
    {
        var policy = BuildPolicy(
            readPrefixes: ["/repo/wiki/"],
            writePrefixes: ["/repo/wiki/tech/"]);

        // Sanity: the original policy permits this write.
        Assert.True(policy.Evaluate("/repo/wiki/tech/foo.md", isWrite: true).IsAllowed);

        var readOnly = policy.WithNoWriteAccess();
        var decision = readOnly.Evaluate("/repo/wiki/tech/foo.md", isWrite: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal("out_of_scope", decision.DenialReason);
    }

    [Fact]
    public void WithNoWriteAccess_PreservesReadAccess_Unchanged()
    {
        var policy = BuildPolicy(
            readPrefixes: ["/repo/wiki/"],
            writePrefixes: ["/repo/wiki/tech/"]);

        var readOnly = policy.WithNoWriteAccess();
        var decision = readOnly.Evaluate("/repo/wiki/tech/foo.md", isWrite: false);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void WithNoWriteAccess_StillDenies_PathTraversal()
    {
        var policy = BuildPolicy(readPrefixes: ["/repo/wiki/"], writePrefixes: ["/repo/wiki/"]);

        var readOnly = policy.WithNoWriteAccess();
        var decision = readOnly.Evaluate("/etc/passwd", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("traversal", decision.DenialReason);
    }

    // ── Prefix matching ───────────────────────────────────────────────────────────

    [Fact]
    public void ReadPrefix_AllowsMatchingPath()
    {
        var policy = BuildPolicy(readPrefixes: ["/repo/wiki/"]);

        var decision = policy.Evaluate("/repo/wiki/tech/foo.md", isWrite: false);

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.DenialReason);
    }

    [Fact]
    public void ReadPrefix_AllowsTheDirectoryItself()
    {
        var policy = BuildPolicy(readPrefixes: ["/repo/wiki/tech/"]);

        // A list_files(path: "tech") call canonicalizes to the bare directory path,
        // with no trailing separator — the directory rule must still match it.
        var decision = policy.Evaluate("/repo/wiki/tech", isWrite: false);

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

    [Fact]
    public void ExactFilePrefix_DeniesSuffixPath()
    {
        var policy = BuildPolicy(readPrefixes: ["/repo/wiki/index.md"]);

        var decision = policy.Evaluate("/repo/wiki/index.md.tmp", isWrite: false);

        Assert.False(decision.IsAllowed);
    }

    // ── Read/write scope separation ───────────────────────────────────────────────

    [Fact]
    public void ReadPrefix_DoesNotGrantWrite()
    {
        var policy = BuildPolicy(
            readPrefixes: ["/repo/wiki/"],
            writePrefixes: []);

        var decision = policy.Evaluate("/repo/wiki/tech/foo.md", isWrite: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal("out_of_scope", decision.DenialReason);
    }

    [Fact]
    public void WritePrefix_DoesNotGrantRead()
    {
        var policy = BuildPolicy(
            readPrefixes: [],
            writePrefixes: ["/repo/wiki/tech/"]);

        var decision = policy.Evaluate("/repo/wiki/tech/foo.md", isWrite: false);

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
        var policy = BuildPolicy(writePrefixes: ["/repo/wiki/tech/"]);

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
