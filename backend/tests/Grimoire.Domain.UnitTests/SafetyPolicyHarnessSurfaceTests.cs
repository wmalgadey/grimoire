using Grimoire.Domain.Guardrails;

namespace Grimoire.Domain.UnitTests;

/// <summary>
/// T058 (022-align-wiki-structure, US3, ADR-023): the denied-read-subtree narrowing —
/// a denied subtree beats a matching read prefix, the bare directory itself is denied
/// (not only files under it, mirroring <see cref="SafetyPolicyTests.ReadPrefix_AllowsTheDirectoryItself"/>'s
/// directory-rule shape), write evaluation is completely unaffected, and the denial check
/// runs before the allow loop (ordering matters for future maintainers, ADR-023's fixed
/// ordering).
/// </summary>
public class SafetyPolicyHarnessSurfaceTests
{
    private const string RepoRoot = "/repo";

    private static SafetyPolicy BuildPolicy(
        string[]? readPrefixes = null,
        string[]? writePrefixes = null,
        string[]? deniedReadSubtrees = null)
        => new(
            repositoryRoot: RepoRoot,
            readPrefixes: readPrefixes ?? [],
            writeRules: (writePrefixes ?? []).Select(p => new WriteRule(p, CreateOnly: false)).ToList(),
            deniedReadSubtrees: deniedReadSubtrees ?? []);

    // ── Denied subtree beats a matching read prefix ────────────────────────────────

    [Fact]
    public void DeniedReadSubtree_DeniesRead_EvenThoughABroaderReadPrefixWouldOtherwiseAllowIt()
    {
        var policy = BuildPolicy(
            readPrefixes: ["/repo/wiki/"],
            deniedReadSubtrees: ["/repo/wiki/tasks/"]);

        var decision = policy.Evaluate("/repo/wiki/tasks/2026-01-01-ingest-abc.md", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("harness_surface_not_granted", decision.DenialReason);
    }

    [Fact]
    public void NonDeniedPathUnderTheSameReadPrefix_IsStillAllowed()
    {
        var policy = BuildPolicy(
            readPrefixes: ["/repo/wiki/"],
            deniedReadSubtrees: ["/repo/wiki/tasks/"]);

        var decision = policy.Evaluate("/repo/wiki/concepts/idempotency.md", isWrite: false);

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.DenialReason);
    }

    // ── The bare directory itself is denied, not only files under it ──────────────

    [Fact]
    public void DeniedReadSubtree_DeniesTheBareDirectoryItself()
    {
        var policy = BuildPolicy(
            readPrefixes: ["/repo/wiki/"],
            deniedReadSubtrees: ["/repo/wiki/tasks/"]);

        // list_files(path: "tasks") canonicalizes to the bare directory path, with no
        // trailing separator — the denied subtree must still match it (mirrors
        // SafetyPolicyTests.ReadPrefix_AllowsTheDirectoryItself's directory-rule shape).
        var decision = policy.Evaluate("/repo/wiki/tasks", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("harness_surface_not_granted", decision.DenialReason);
    }

    // ── Multiple denied subtrees ────────────────────────────────────────────────────

    [Fact]
    public void MultipleDeniedReadSubtrees_EachDeniesItsOwnSubtree()
    {
        var policy = BuildPolicy(
            readPrefixes: ["/repo/wiki/"],
            deniedReadSubtrees: ["/repo/wiki/tasks/", "/repo/wiki/conversations/", "/repo/wiki/findings/", "/repo/wiki/remediation-tasks/"]);

        Assert.Equal("harness_surface_not_granted", policy.Evaluate("/repo/wiki/tasks/x.md", isWrite: false).DenialReason);
        Assert.Equal("harness_surface_not_granted", policy.Evaluate("/repo/wiki/conversations/x.md", isWrite: false).DenialReason);
        Assert.Equal("harness_surface_not_granted", policy.Evaluate("/repo/wiki/findings/x.md", isWrite: false).DenialReason);
        Assert.Equal("harness_surface_not_granted", policy.Evaluate("/repo/wiki/remediation-tasks/x.md", isWrite: false).DenialReason);
        Assert.True(policy.Evaluate("/repo/wiki/index.md", isWrite: false).IsAllowed);
    }

    [Fact]
    public void EmptyDeniedReadSubtrees_BehavesExactlyLikePreAdr023()
    {
        var policy = BuildPolicy(readPrefixes: ["/repo/wiki/"], deniedReadSubtrees: []);

        var decision = policy.Evaluate("/repo/wiki/tasks/x.md", isWrite: false);

        Assert.True(decision.IsAllowed);
    }

    // ── Write evaluation is completely unaffected ───────────────────────────────────

    [Fact]
    public void DeniedReadSubtree_DoesNotAffectWriteEvaluation_AllowedWriteStillAllowed()
    {
        var policy = BuildPolicy(
            writePrefixes: ["/repo/wiki/tasks/"],
            deniedReadSubtrees: ["/repo/wiki/tasks/"]);

        var decision = policy.Evaluate("/repo/wiki/tasks/x.md", isWrite: true);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void DeniedReadSubtree_DoesNotAffectWriteEvaluation_DisallowedWriteStaysOutOfScope()
    {
        var policy = BuildPolicy(
            writePrefixes: [],
            deniedReadSubtrees: ["/repo/wiki/tasks/"]);

        var decision = policy.Evaluate("/repo/wiki/tasks/x.md", isWrite: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal("out_of_scope", decision.DenialReason);
    }

    // ── Ordering: the denial check runs before the allow loop ──────────────────────

    [Fact]
    public void DeniedReadSubtree_TakesPriority_EvenWhenAnExactReadPrefixAlsoMatches()
    {
        // An exact-match read prefix for a file inside the denied subtree would allow it
        // if the allow loop ran first — proves the denial check is genuinely evaluated
        // BEFORE the allow loop, not merely "also present".
        var policy = BuildPolicy(
            readPrefixes: ["/repo/wiki/tasks/x.md"],
            deniedReadSubtrees: ["/repo/wiki/tasks/"]);

        var decision = policy.Evaluate("/repo/wiki/tasks/x.md", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("harness_surface_not_granted", decision.DenialReason);
    }

    // ── Traversal still wins over everything ────────────────────────────────────────

    [Fact]
    public void DeniedReadSubtree_DoesNotSuppress_TraversalDenial()
    {
        var policy = BuildPolicy(
            readPrefixes: ["/repo/wiki/"],
            deniedReadSubtrees: ["/repo/wiki/tasks/"]);

        var decision = policy.Evaluate("/etc/passwd", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("traversal", decision.DenialReason);
    }

    // ── WithDeniedReadSubtrees (runtime narrowing, modelled on WithNoWriteAccess) ───

    [Fact]
    public void WithDeniedReadSubtrees_NarrowsAnAlreadyLoadedPolicy_WithoutAffectingWriteRules()
    {
        var policy = BuildPolicy(
            readPrefixes: ["/repo/wiki/"],
            writePrefixes: ["/repo/wiki/tasks/"]);

        // Sanity: before narrowing, this read is allowed.
        Assert.True(policy.Evaluate("/repo/wiki/tasks/x.md", isWrite: false).IsAllowed);

        var narrowed = policy.WithDeniedReadSubtrees(["/repo/wiki/tasks/"]);

        var readDecision = narrowed.Evaluate("/repo/wiki/tasks/x.md", isWrite: false);
        Assert.False(readDecision.IsAllowed);
        Assert.Equal("harness_surface_not_granted", readDecision.DenialReason);

        // Write rules untouched by the read-scope narrowing.
        var writeDecision = narrowed.Evaluate("/repo/wiki/tasks/x.md", isWrite: true);
        Assert.True(writeDecision.IsAllowed);
    }

    [Fact]
    public void WithDeniedReadSubtrees_PreservesReadAccess_ToPathsOutsideTheDeniedSubtree()
    {
        var policy = BuildPolicy(readPrefixes: ["/repo/wiki/"]);

        var narrowed = policy.WithDeniedReadSubtrees(["/repo/wiki/tasks/"]);

        var decision = narrowed.Evaluate("/repo/wiki/concepts/idempotency.md", isWrite: false);
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void WithNoWriteAccess_ComposedWithWithDeniedReadSubtrees_AppliesBothNarrowings()
    {
        // ADR-018 message-turn mode chains WithNoWriteAccess; ADR-023's read-scope
        // narrowing must compose with it (both applied via AgentHost) without either
        // narrowing undoing the other.
        var policy = BuildPolicy(
            readPrefixes: ["/repo/wiki/"],
            writePrefixes: ["/repo/wiki/tasks/"],
            deniedReadSubtrees: ["/repo/wiki/tasks/"]);

        var readOnlyAndScoped = policy.WithNoWriteAccess();

        Assert.False(readOnlyAndScoped.Evaluate("/repo/wiki/tasks/x.md", isWrite: true).IsAllowed);
        var readDecision = readOnlyAndScoped.Evaluate("/repo/wiki/tasks/x.md", isWrite: false);
        Assert.False(readDecision.IsAllowed);
        Assert.Equal("harness_surface_not_granted", readDecision.DenialReason);
    }
}
