namespace Grimoire.ArchTests;

/// <summary>
/// Structural regression guard for ADR-021 (SlowEval class set as amended by ADR-033) /
/// spec 019-fast-test-tier FR-014 and SC-002:
/// tier membership inside <c>Grimoire.AgentEvals</c> is declared by the <c>Tier</c> xUnit
/// trait, not by which file a test lives in (research.md R1). This rule makes that
/// membership a permanent, CI-enforced fact instead of a one-time manual check —
/// reflection-based (not IL-scan) since it only inspects type-level custom attributes,
/// which <see cref="System.Reflection.MemberInfo.GetCustomAttributesData"/> reads directly.
/// </summary>
public class AgentEvalsTierMembershipRuleTests
{
    // The hermetic harness-mechanics classes (T002) that must carry Tier=Fast so
    // `scripts/test-fast.sh`'s `--filter "Tier=Fast"` selects them and zero replay-eval
    // prerequisite is ever required to run the fast tier. (The scorer-unit classes for
    // the removed lower-stakes scenarios left this set with their scenarios —
    // Constitution Principle II, v1.12.0.)
    private static readonly Type[] _hermeticFastTierTypes =
    [
        typeof(Grimoire.AgentEvals.ReplayContractTests),
        typeof(Grimoire.AgentEvals.CaptureHygieneTests),
        typeof(Grimoire.AgentEvals.StalenessTests),
        typeof(Grimoire.AgentEvals.EvalProviderResolverTests),
        typeof(Grimoire.AgentEvals.RemediationReVerificationScorerTests),
        typeof(Grimoire.AgentEvals.LocalEnvFileTests),
        typeof(Grimoire.AgentEvals.TimeoutEnforcingModelClientTests),
    ];

    // The genuine replay-eval scenario classes (T014/US3) — agent-judgment evals,
    // never selected by the fast tier's Tier=Fast filter.
    private static readonly Type[] _replayEvalTypes =
    [
        typeof(Grimoire.AgentEvals.IngestReplayEvalTests),
        typeof(Grimoire.AgentEvals.LintReplayEvalTests),
        typeof(Grimoire.AgentEvals.QueryReplayEvalTests),
        typeof(Grimoire.AgentEvals.RemediationReVerificationEvalTests),
    ];

    // Collapsed from four directional membership checks to two exact-set checks
    // (constitution v1.9.0 "Test what we own" — the xUnit-trait-facing wire-up allowance):
    // each of the four originals asserted only one direction of a set-equality fact
    // (member has the trait / non-member lacks it), so two of them were always redundant
    // with the other two once both sides of a partition are known. Asserting the exact
    // Tier=Fast/Tier=SlowEval membership set in one place each proves the same FR-014/
    // SC-002 fact without the duplication.

    [Fact]
    public void TierFastTrait_IsCarriedByExactlyTheHermeticHarnessMechanicsClasses()
    {
        var missing = _hermeticFastTierTypes.Where(t => !HasTrait(t, "Tier", "Fast")).ToList();
        var wronglyTagged = _replayEvalTypes.Where(t => HasTrait(t, "Tier", "Fast")).ToList();

        Assert.True(
            missing.Count == 0 && wronglyTagged.Count == 0,
            "FR-014/SC-002: [Trait(\"Tier\", \"Fast\")] must be carried by exactly the hermetic " +
            "harness-mechanics classes, so scripts/test-fast.sh's --filter \"Tier=Fast\" selects " +
            "them and only them (the fast tier must execute zero agent-evaluation tests). " +
            "Missing: " + string.Join(", ", missing.Select(t => t.FullName)) +
            "; wrongly tagged: " + string.Join(", ", wronglyTagged.Select(t => t.FullName)));
    }

    // T017 (US3): the opt-in slow tier's documented command
    // (`dotnet test ... --filter "Tier=SlowEval"`) selects exactly this set, permanently.
    [Fact]
    public void TierSlowEvalTrait_IsCarriedByExactlyTheReplayEvalClasses()
    {
        var missing = _replayEvalTypes.Where(t => !HasTrait(t, "Tier", "SlowEval")).ToList();
        var wronglyTagged = _hermeticFastTierTypes.Where(t => HasTrait(t, "Tier", "SlowEval")).ToList();

        Assert.True(
            missing.Count == 0 && wronglyTagged.Count == 0,
            "FR-014: [Trait(\"Tier\", \"SlowEval\")] must be carried by exactly the replay-eval " +
            "scenario classes, so `dotnet test ... --filter \"Tier=SlowEval\"` selects them and " +
            "only them. Missing: " + string.Join(", ", missing.Select(t => t.FullName)) +
            "; wrongly tagged: " + string.Join(", ", wronglyTagged.Select(t => t.FullName)));
    }

    private static bool HasTrait(Type type, string name, string value) =>
        type.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType.FullName == "Xunit.TraitAttribute" &&
            attribute.ConstructorArguments.Count == 2 &&
            string.Equals(attribute.ConstructorArguments[0].Value as string, name, StringComparison.Ordinal) &&
            string.Equals(attribute.ConstructorArguments[1].Value as string, value, StringComparison.Ordinal));
}
