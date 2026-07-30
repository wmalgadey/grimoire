namespace Grimoire.EvalRunner.Scenarios;

/// <summary>
/// One evaluated Lint-agent scenario (013-lint-agent, T017/T018). Unlike Ingest's
/// <see cref="ScenarioDefinition"/> (one pasted source) or Query's
/// <see cref="QueryScenarioDefinition"/> (a turn sequence), a Lint scenario takes no
/// per-run input at all — its "input" is the whole seeded wiki fixture at
/// <see cref="FixtureName"/>, read by <c>list_files</c>/<c>read_file</c> once the run
/// starts (FR-002). Every sample re-runs against the same fixture (sampling
/// nondeterminism only), mirroring <c>QueryScenarioDefinition</c>'s single-fixed-sequence
/// case.
/// </summary>
public sealed record LintScenarioDefinition(
    string Id,
    string FixtureName,
    double Threshold,
    string ScorerId)
{
    /// <summary>
    /// Stable serialization for the `scenario_definition` staleness fingerprint (mirrors
    /// <see cref="ScenarioDefinition.StableSerialization"/>/<see cref="QueryScenarioDefinition.StableSerialization"/>).
    /// </summary>
    public string StableSerialization() =>
        $"id={Id}\nfixture={FixtureName}\nthreshold={Threshold:0.00}\nscorer={ScorerId}\n";
}

/// <summary>
/// The Lint eval scenarios (013-lint-agent Test Strategy, SC-005/SC-006). Both reuse the
/// same seeded-defect fixture (<c>lint-seeded-defects</c>,
/// <c>backend/tests/Grimoire.AgentEvals/Fixtures/lint-seeded-defects/wiki/</c>), which
/// carries one instance of each defect category named in the spec: a contradiction pair
/// (<c>cache-invalidation-ttl</c>/<c>cache-invalidation-events</c>), a missing
/// cross-reference pair (<c>retry-backoff</c>/<c>circuit-breaker</c>), an orphan page
/// (<c>orphan-topic</c>), a page missing tags (<c>undertagged-topic</c>), a page missing
/// confidence (<c>unscored-topic</c>), and a stale low-confidence page
/// (<c>stale-topic</c>, <c>last_reviewed</c> far outside the 90-day Review Window).
/// </summary>
public static class LintScenarioDefinitions
{
    public const string SeededDefectsFixtureName = "lint-seeded-defects";

    /// <summary>SC-005: ≥ 85% of seeded defects found, per category.</summary>
    public static readonly LintScenarioDefinition DefectsFound = new(
        Id: "lint-defects-found",
        FixtureName: SeededDefectsFixtureName,
        Threshold: 0.85,
        ScorerId: "lint-defects-found");

    /// <summary>SC-006: ≥ 90% of sampled findings are genuine — the described problem exists in the pages named.</summary>
    public static readonly LintScenarioDefinition GenuineFindings = new(
        Id: "lint-genuine-findings",
        FixtureName: SeededDefectsFixtureName,
        Threshold: 0.90,
        ScorerId: "lint-genuine-findings");

    public static readonly IReadOnlyList<LintScenarioDefinition> All = [DefectsFound, GenuineFindings];

    public static LintScenarioDefinition? Find(string scenarioId)
        => All.FirstOrDefault(s => string.Equals(s.Id, scenarioId, StringComparison.OrdinalIgnoreCase));
}
