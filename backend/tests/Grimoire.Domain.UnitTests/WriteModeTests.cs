using Grimoire.Domain.Guardrails;

namespace Grimoire.Domain.UnitTests;

/// <summary>
/// T006 (013-lint-agent, ADR-016): the three-way <see cref="WriteMode"/> extension — a
/// <c>frontmatter-only</c> rule surfaces <see cref="PolicyDecision.Mode"/> ==
/// <see cref="WriteMode.FrontmatterOnly"/> on allow and <see cref="PolicyDecision.IsCreateOnly"/>
/// == <c>false</c>; existing <c>read-write</c>/<c>create-only</c> behavior (including the
/// <c>IsCreateOnly</c> convenience) is unchanged, confirmed by re-running the full existing
/// <see cref="SafetyPolicyModeTests"/> suite unmodified alongside these new cases.
/// </summary>
public class WriteModeTests
{
    private const string RepoRoot = "/repo";

    [Fact]
    public void FrontmatterOnlyWriteRule_Allow_SurfacesModeFrontmatterOnly_AndIsCreateOnlyFalse()
    {
        var policy = new SafetyPolicy(
            RepoRoot,
            readPrefixes: [],
            writeRules: [new WriteRule("/repo/wiki/pages/", WriteMode.FrontmatterOnly)]);

        var decision = policy.Evaluate("/repo/wiki/pages/existing.md", isWrite: true);

        Assert.True(decision.IsAllowed);
        Assert.Equal(WriteMode.FrontmatterOnly, decision.Mode);
        Assert.False(decision.IsCreateOnly);
    }

    [Fact]
    public void CreateOnlyWriteRule_Allow_SurfacesModeCreateOnly()
    {
        var policy = new SafetyPolicy(
            RepoRoot,
            readPrefixes: [],
            writeRules: [new WriteRule("/repo/wiki/pages/", WriteMode.CreateOnly)]);

        var decision = policy.Evaluate("/repo/wiki/pages/new.md", isWrite: true);

        Assert.True(decision.IsAllowed);
        Assert.Equal(WriteMode.CreateOnly, decision.Mode);
        Assert.True(decision.IsCreateOnly);
    }

    [Fact]
    public void ReadWriteWriteRule_Allow_SurfacesModeReadWrite()
    {
        var policy = new SafetyPolicy(
            RepoRoot,
            readPrefixes: [],
            writeRules: [new WriteRule("/repo/wiki/index.md", WriteMode.ReadWrite)]);

        var decision = policy.Evaluate("/repo/wiki/index.md", isWrite: true);

        Assert.True(decision.IsAllowed);
        Assert.Equal(WriteMode.ReadWrite, decision.Mode);
        Assert.False(decision.IsCreateOnly);
    }

    [Fact]
    public void WriteRule_BooleanConstructor_CreateOnlyTrue_SurfacesModeCreateOnly()
    {
        // Backward compatibility (ADR-016): the pre-existing bool constructor shape must
        // still produce the equivalent Mode value.
        var rule = new WriteRule("/repo/wiki/pages/", CreateOnly: true);

        Assert.Equal(WriteMode.CreateOnly, rule.Mode);
        Assert.True(rule.CreateOnly);
    }

    [Fact]
    public void WriteRule_BooleanConstructor_CreateOnlyFalse_SurfacesModeReadWrite()
    {
        var rule = new WriteRule("/repo/wiki/index.md", CreateOnly: false);

        Assert.Equal(WriteMode.ReadWrite, rule.Mode);
        Assert.False(rule.CreateOnly);
    }

    [Fact]
    public void PolicyDecision_Allow_BooleanOverload_SurfacesEquivalentMode()
    {
        // Backward compatibility (ADR-016): PolicyDecision.Allow(isCreateOnly: bool) must
        // still produce the equivalent Mode/IsCreateOnly pair.
        var allowCreateOnly = PolicyDecision.Allow(isCreateOnly: true);
        var allowReadWrite = PolicyDecision.Allow(isCreateOnly: false);

        Assert.Equal(WriteMode.CreateOnly, allowCreateOnly.Mode);
        Assert.True(allowCreateOnly.IsCreateOnly);

        Assert.Equal(WriteMode.ReadWrite, allowReadWrite.Mode);
        Assert.False(allowReadWrite.IsCreateOnly);
    }

    [Fact]
    public void FrontmatterOnlyRule_DoesNotAffect_TraversalDenial()
    {
        var policy = new SafetyPolicy(
            RepoRoot,
            readPrefixes: [],
            writeRules: [new WriteRule("/repo/wiki/pages/", WriteMode.FrontmatterOnly)]);

        var decision = policy.Evaluate("/etc/passwd", isWrite: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal("traversal", decision.DenialReason);
        Assert.False(decision.IsCreateOnly);
    }

    [Fact]
    public void FrontmatterOnlyRule_DoesNotAffect_OutOfScopeDenial()
    {
        var policy = new SafetyPolicy(
            RepoRoot,
            readPrefixes: [],
            writeRules: [new WriteRule("/repo/wiki/pages/", WriteMode.FrontmatterOnly)]);

        var decision = policy.Evaluate("/repo/wiki/index.md", isWrite: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal("out_of_scope", decision.DenialReason);
    }
}
