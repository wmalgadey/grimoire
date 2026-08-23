using Grimoire.Domain.Guardrails;

namespace Grimoire.Domain.UnitTests;

/// <summary>
/// T007 (026-guarded-tool-surface, ADR-031 R3, data-model.md): <c>SafetyPolicy.EvaluateDelete</c>
/// is a pure, dependency-free decision — deny-by-default like every other scope, and never
/// derived from the write scope. Mirrors <see cref="SafetyPolicyTests"/>'s idiom.
/// </summary>
public class DeleteRuleTests
{
    private const string RepoRoot = "/repo";

    private static SafetyPolicy BuildPolicy(
        string[]? readPrefixes = null,
        string[]? writePrefixes = null,
        DeleteRule[]? deleteRules = null)
        => new(
            repositoryRoot: RepoRoot,
            readPrefixes: readPrefixes ?? [],
            writeRules: (writePrefixes ?? []).Select(p => new WriteRule(p)).ToList(),
            deleteRules: deleteRules ?? []);

    [Fact]
    public void EmptyDeleteRules_DeniesDeleteRequest_WithNoRule()
    {
        var policy = BuildPolicy();

        var decision = policy.EvaluateDelete("/repo/wiki/tech/foo.md");

        Assert.False(decision.IsAllowed);
        Assert.Equal("no_rule", decision.DenialReason);
    }

    [Fact]
    public void MatchingDeletePrefix_AllowsDeleteRequest()
    {
        var policy = BuildPolicy(deleteRules: [new DeleteRule("/repo/wiki/")]);

        var decision = policy.EvaluateDelete("/repo/wiki/tech/foo.md");

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void WriteScopeAlone_NeverGrantsDeletion()
    {
        // The load-bearing regression this scope split exists for (research.md D6): a
        // read-write rule on the same prefix must confer no deletion at all.
        var policy = BuildPolicy(writePrefixes: ["/repo/wiki/"]);

        var writeDecision = policy.Evaluate("/repo/wiki/tech/foo.md", isWrite: true);
        var deleteDecision = policy.EvaluateDelete("/repo/wiki/tech/foo.md");

        Assert.True(writeDecision.IsAllowed);
        Assert.False(deleteDecision.IsAllowed);
        Assert.Equal("no_rule", deleteDecision.DenialReason);
    }

    [Fact]
    public void ExcludedPrefix_IsNeverDeletable_EvenWhenTheDeleteRuleOtherwiseMatches()
    {
        var policy = BuildPolicy(deleteRules:
        [
            new DeleteRule("/repo/wiki/", ExcludePrefixes: ["/repo/wiki/index.md"]),
        ]);

        var excluded = policy.EvaluateDelete("/repo/wiki/index.md");
        var included = policy.EvaluateDelete("/repo/wiki/tech/foo.md");

        Assert.False(excluded.IsAllowed);
        Assert.Equal("no_rule", excluded.DenialReason);
        Assert.True(included.IsAllowed);
    }

    [Fact]
    public void PathEscapingRepositoryRoot_IsDeniedAsTraversal_RegardlessOfDeleteRules()
    {
        var policy = BuildPolicy(deleteRules: [new DeleteRule("/repo/wiki/")]);

        var decision = policy.EvaluateDelete("/etc/passwd");

        Assert.False(decision.IsAllowed);
        Assert.Equal("traversal", decision.DenialReason);
    }

    [Fact]
    public void WithNoWriteAccess_AlsoStripsDeleteRules()
    {
        var policy = BuildPolicy(deleteRules: [new DeleteRule("/repo/wiki/")]);
        var readOnly = policy.WithNoWriteAccess();

        Assert.True(policy.EvaluateDelete("/repo/wiki/tech/foo.md").IsAllowed);

        var decision = readOnly.EvaluateDelete("/repo/wiki/tech/foo.md");
        Assert.False(decision.IsAllowed);
        Assert.Equal("no_rule", decision.DenialReason);
    }
}
