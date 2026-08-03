namespace Grimoire.ArchTests;

/// <summary>
/// Structural regression guard for ADR-021 / spec 019-fast-test-tier FR-014 and SC-002:
/// tier membership inside <c>Grimoire.AgentEvals</c> is declared by the <c>Tier</c> xUnit
/// trait, not by which file a test lives in (research.md R1). This rule makes that
/// membership a permanent, CI-enforced fact instead of a one-time manual check —
/// reflection-based (not IL-scan) since it only inspects type-level custom attributes,
/// which <see cref="System.Reflection.MemberInfo.GetCustomAttributesData"/> reads directly.
/// </summary>
public class AgentEvalsTierMembershipRuleTests
{
    // The nine hermetic harness-mechanics classes (T002) that must carry Tier=Fast so
    // `scripts/test-fast.sh`'s `--filter "Tier=Fast"` selects them and zero replay-eval
    // prerequisite is ever required to run the fast tier.
    private static readonly Type[] _hermeticFastTierTypes =
    [
        typeof(Grimoire.AgentEvals.ReplayContractTests),
        typeof(Grimoire.AgentEvals.CaptureHygieneTests),
        typeof(Grimoire.AgentEvals.StalenessTests),
        typeof(Grimoire.AgentEvals.EvalProviderResolverTests),
        typeof(Grimoire.AgentEvals.EvalCredentialRedactionTests),
        typeof(Grimoire.AgentEvals.LintDeterministicScorersTests),
        typeof(Grimoire.AgentEvals.RemediationReVerificationScorerTests),
        typeof(Grimoire.AgentEvals.LocalEnvFileTests),
        typeof(Grimoire.AgentEvals.TimeoutEnforcingModelClientTests),
    ];

    // The five genuine replay-eval scenario classes (T014/US3) — agent-judgment evals,
    // never selected by the fast tier's Tier=Fast filter.
    private static readonly Type[] _replayEvalTypes =
    [
        typeof(Grimoire.AgentEvals.IngestReplayEvalTests),
        typeof(Grimoire.AgentEvals.LintReplayEvalTests),
        typeof(Grimoire.AgentEvals.QueryReplayEvalTests),
        typeof(Grimoire.AgentEvals.LintRemediationProposalRelevanceEvalTests),
        typeof(Grimoire.AgentEvals.RemediationReVerificationEvalTests),
    ];

    [Fact]
    public void HermeticHarnessMechanicsClasses_MustCarryTierFastTrait()
    {
        var missing = _hermeticFastTierTypes.Where(t => !HasTrait(t, "Tier", "Fast")).ToList();

        Assert.True(
            missing.Count == 0,
            "FR-014/SC-002: every hermetic harness-mechanics class in Grimoire.AgentEvals " +
            "must carry [Trait(\"Tier\", \"Fast\")] so scripts/test-fast.sh selects it. " +
            "Missing: " + string.Join(", ", missing.Select(t => t.FullName)));
    }

    [Fact]
    public void ReplayEvalClasses_MustNotCarryTierFastTrait()
    {
        var wronglyTagged = _replayEvalTypes.Where(t => HasTrait(t, "Tier", "Fast")).ToList();

        Assert.True(
            wronglyTagged.Count == 0,
            "FR-014/SC-002: replay-eval scenario classes must never carry " +
            "[Trait(\"Tier\", \"Fast\")] — the fast tier must execute zero agent-evaluation " +
            "tests. Wrongly tagged: " + string.Join(", ", wronglyTagged.Select(t => t.FullName)));
    }

    private static bool HasTrait(Type type, string name, string value) =>
        type.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType.FullName == "Xunit.TraitAttribute" &&
            attribute.ConstructorArguments.Count == 2 &&
            string.Equals(attribute.ConstructorArguments[0].Value as string, name, StringComparison.Ordinal) &&
            string.Equals(attribute.ConstructorArguments[1].Value as string, value, StringComparison.Ordinal));
}
