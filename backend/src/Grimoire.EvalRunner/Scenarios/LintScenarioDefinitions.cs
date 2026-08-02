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

    /// <summary>
    /// T032 (013-lint-agent, US2): a dedicated fixture with a known cross-link graph and
    /// deliberately stale recorded <c>inbound_links</c> counts
    /// (<c>backend/tests/Grimoire.AgentEvals/Fixtures/lint-inbound-links-fixture/wiki/</c>)
    /// — three pages (<c>hub-page</c>, <c>spoke-a</c>, <c>spoke-b</c>) whose true
    /// inbound-link counts (3/2/1) are mechanically computable from <c>index.md</c> and
    /// each page's own body, every one of them recorded wrong on disk before a run.
    /// </summary>
    public const string InboundLinksFixtureName = "lint-inbound-links-fixture";

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

    /// <summary>
    /// T032: SC-007 — ≥ 90% of sampled tag proposals conform to the tag taxonomy, and
    /// ≥ 90% of sampled confidence proposals conform to the confidence-scoring
    /// convention. Reuses <see cref="SeededDefectsFixtureName"/>'s <c>undertagged-topic</c>
    /// (missing tags) and <c>unscored-topic</c> (missing confidence) pages.
    /// </summary>
    public static readonly LintScenarioDefinition MetadataProposals = new(
        Id: "lint-metadata-proposals",
        FixtureName: SeededDefectsFixtureName,
        Threshold: 0.90,
        ScorerId: "lint-metadata-proposals");

    /// <summary>
    /// T033: SC-008 — ≥ 95% of sampled pages have an accurate inbound-link count after a
    /// run, scored against <see cref="InboundLinksFixtureName"/>'s known graph.
    /// </summary>
    public static readonly LintScenarioDefinition InboundLinksRefreshed = new(
        Id: "lint-inbound-links-refreshed",
        FixtureName: InboundLinksFixtureName,
        Threshold: 0.95,
        ScorerId: "lint-inbound-links-refreshed");

    /// <summary>
    /// T028 (015-lint-board-parity, SC-006): ≥ 90% of sampled proposed remediation
    /// tasks are relevant/actionable, scored against
    /// <see cref="Scoring.RemediationGoldenSet"/>'s human-adjudicated-once judgment of
    /// the <see cref="SeededDefectsFixtureName"/> fixture's six seeded defects. Reuses
    /// the same fixture as <see cref="DefectsFound"/>/<see cref="GenuineFindings"/>/
    /// <see cref="MetadataProposals"/> — the run's `proposedActions` are a new field on
    /// the same terminal event those scenarios already score, not a new agent behavior
    /// to fixture separately.
    /// </summary>
    public static readonly LintScenarioDefinition RemediationProposalsRelevant = new(
        Id: "lint-remediation-proposals",
        FixtureName: SeededDefectsFixtureName,
        Threshold: 0.90,
        ScorerId: "lint-remediation-proposals-relevant");

    public static readonly IReadOnlyList<LintScenarioDefinition> All =
        [DefectsFound, GenuineFindings, MetadataProposals, InboundLinksRefreshed, RemediationProposalsRelevant];

    public static LintScenarioDefinition? Find(string scenarioId)
        => All.FirstOrDefault(s => string.Equals(s.Id, scenarioId, StringComparison.OrdinalIgnoreCase));
}
