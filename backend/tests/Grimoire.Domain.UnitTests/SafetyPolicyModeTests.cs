using Grimoire.Domain.Guardrails;

namespace Grimoire.Domain.UnitTests;

/// <summary>
/// T009 (012-query-synthesis-writes, ADR-015): the write-rule <c>mode</c> extension —
/// a create-only rule surfaces <see cref="PolicyDecision.IsCreateOnly"/> on allow; a plain
/// (mode-absent) rule surfaces <c>false</c>; read-scope decisions never carry the flag;
/// existing denial reasons are unaffected.
/// </summary>
public class SafetyPolicyModeTests
{
    private const string RepoRoot = "/repo";

    [Fact]
    public void CreateOnlyWriteRule_Allow_SurfacesIsCreateOnlyTrue()
    {
        var policy = new SafetyPolicy(
            RepoRoot,
            readPrefixes: [],
            writeRules: [new WriteRule("/repo/wiki/pages/", CreateOnly: true)]);

        var decision = policy.Evaluate("/repo/wiki/pages/new.md", isWrite: true);

        Assert.True(decision.IsAllowed);
        Assert.True(decision.IsCreateOnly);
    }

    [Fact]
    public void PlainWriteRule_ModeAbsent_Allow_SurfacesIsCreateOnlyFalse()
    {
        var policy = new SafetyPolicy(
            RepoRoot,
            readPrefixes: [],
            writeRules: [new WriteRule("/repo/wiki/index.md", CreateOnly: false)]);

        var decision = policy.Evaluate("/repo/wiki/index.md", isWrite: true);

        Assert.True(decision.IsAllowed);
        Assert.False(decision.IsCreateOnly);
    }

    [Fact]
    public void LegacyStringPrefixConstructor_Allow_SurfacesIsCreateOnlyFalse()
    {
        // Backward compatibility: the plain-string-list constructor (used by every
        // pre-existing caller, e.g. data/agents/ingest/policy.json with no "mode" field)
        // must behave exactly as read-write, byte for byte.
        var policy = new SafetyPolicy(
            RepoRoot,
            readPrefixes: [],
            writePrefixes: ["/repo/wiki/pages/"]);

        var decision = policy.Evaluate("/repo/wiki/pages/existing.md", isWrite: true);

        Assert.True(decision.IsAllowed);
        Assert.False(decision.IsCreateOnly);
    }

    [Fact]
    public void ReadScopeDecision_NeverCarriesCreateOnlyFlag()
    {
        var policy = new SafetyPolicy(
            RepoRoot,
            readPrefixes: ["/repo/wiki/"],
            writeRules: [new WriteRule("/repo/wiki/pages/", CreateOnly: true)]);

        var decision = policy.Evaluate("/repo/wiki/pages/foo.md", isWrite: false);

        Assert.True(decision.IsAllowed);
        Assert.False(decision.IsCreateOnly);
    }

    [Fact]
    public void CreateOnlyRule_DoesNotAffect_TraversalDenial()
    {
        var policy = new SafetyPolicy(
            RepoRoot,
            readPrefixes: [],
            writeRules: [new WriteRule("/repo/wiki/pages/", CreateOnly: true)]);

        var decision = policy.Evaluate("/etc/passwd", isWrite: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal("traversal", decision.DenialReason);
        Assert.False(decision.IsCreateOnly);
    }

    [Fact]
    public void CreateOnlyRule_DoesNotAffect_OutOfScopeDenial()
    {
        var policy = new SafetyPolicy(
            RepoRoot,
            readPrefixes: [],
            writeRules: [new WriteRule("/repo/wiki/pages/", CreateOnly: true)]);

        var decision = policy.Evaluate("/repo/wiki/index.md", isWrite: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal("out_of_scope", decision.DenialReason);
        Assert.False(decision.IsCreateOnly);
    }

    [Fact]
    public void NoRuleDenial_ForRead_IsUnaffectedByWriteModeRules()
    {
        var policy = new SafetyPolicy(
            RepoRoot,
            readPrefixes: [],
            writeRules: [new WriteRule("/repo/wiki/pages/", CreateOnly: true)]);

        var decision = policy.Evaluate("/repo/wiki/pages/foo.md", isWrite: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("no_rule", decision.DenialReason);
    }
}
