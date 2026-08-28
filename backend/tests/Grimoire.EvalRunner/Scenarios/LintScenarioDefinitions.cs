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
    string ScorerId,
    int? ContextBudgetTokens = null)
{
    /// <summary>
    /// Stable serialization for the `scenario_definition` staleness fingerprint (mirrors
    /// <see cref="ScenarioDefinition.StableSerialization"/>/<see cref="QueryScenarioDefinition.StableSerialization"/>).
    /// <see cref="ContextBudgetTokens"/> is appended only when set, so introducing the
    /// field left every pre-existing scenario's fingerprint byte-identical — a scenario
    /// that does not use the lever is not a different scenario for having gained the
    /// option of one.
    /// </summary>
    public string StableSerialization()
    {
        var text =
            $"id={Id}\nfixture={FixtureName}\nthreshold={Threshold.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}\nscorer={ScorerId}\n";
        return ContextBudgetTokens is { } budget
            ? text + $"context_budget_tokens={budget.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n"
            : text;
    }
}

/// <summary>
/// The Lint eval scenarios. Only the high-stakes survey-at-scale scenario remains
/// (Constitution Principle II, v1.12.0): the lower-stakes scenarios were removed in favor
/// of the user-reported correction loop. The seeded-defect fixture
/// (<c>backend/tests/Grimoire.AgentEvals/Fixtures/lint-seeded-defects/wiki/</c>) stays:
/// it is the source tree <see cref="Workspace.LintAtScaleFixture"/> copies verbatim, and
/// its six seeded defects are what the at-scale survey must still find.
/// </summary>
public static class LintScenarioDefinitions
{
    /// <summary>
    /// T006/T066 (026-guarded-tool-surface, SC-011): the survey-at-scale scenario — "on a
    /// wiki larger than the run's context guard, the agent completes its survey while the
    /// content it reads stays under that guard."
    ///
    /// <para><b>The budget is the lever.</b> "Larger than the context guard" is a relation
    /// between two numbers, not a corpus size: rather than authoring a wiki big enough to
    /// strain the agent's real 200k-token window, the scenario declares a much smaller
    /// budget and the generated fixture (<see cref="Workspace.LintAtScaleFixture"/>) only
    /// has to exceed <em>that</em>. Fixture size is therefore irrelevant to the property
    /// under test — turning the lever, not growing the corpus, is how this scenario is
    /// made harder.</para>
    ///
    /// <para><b>What the budget is measured against</b> is the peak recorded
    /// <c>input_tokens</c> across the run's turns — the live conversation size, which is
    /// exactly the quantity <c>AgentLoop</c>'s own context guard compares against its cap
    /// (whole conversation re-sent per request, never summed across turns). Reading the
    /// whole generated wiki costs several times this budget; narrowing with
    /// <c>search_files</c> and ranged reads keeps a thorough survey under it.</para>
    ///
    /// <para>Scored on <em>both</em> halves of SC-011 — the survey must still find the
    /// seeded defects (it is a survey, not a token-frugality contest) <em>and</em> stay
    /// under budget. Narrowing that misses the defects fails, and finding the defects by
    /// reading everything fails.</para>
    /// </summary>
    public static readonly LintScenarioDefinition AtScaleSurvey = new(
        Id: "lint-at-scale-survey",
        FixtureName: Workspace.LintAtScaleFixture.FixtureName,
        Threshold: 0.90,
        ScorerId: "lint-at-scale-survey",
        ContextBudgetTokens: 20_000);

    /// <summary>
    /// 028-lint-at-scale (US1, FR-008/SC-003): the same fixture and scorer as
    /// <see cref="AtScaleSurvey"/>, with the budget lever turned tighter — half the context
    /// budget against the identical, unchanged fixture. Proves the "reading stays bounded
    /// regardless of corpus size" property generalizes across more than one point on the
    /// budget-to-content-size relation, without generating a second, larger corpus
    /// (research.md R3: a large corpus was judged disproportionate to what this needs
    /// proven). Reading-volume growth between this and <see cref="AtScaleSurvey"/> must not
    /// be super-linear as the budget fraction shrinks.
    /// </summary>
    public static readonly LintScenarioDefinition AtScaleSurveyTightBudget = new(
        Id: "lint-at-scale-survey-tight-budget",
        FixtureName: Workspace.LintAtScaleFixture.FixtureName,
        Threshold: 0.90,
        ScorerId: "lint-at-scale-survey",
        ContextBudgetTokens: 10_000);

    public static readonly IReadOnlyList<LintScenarioDefinition> All = [AtScaleSurvey, AtScaleSurveyTightBudget];

    public static LintScenarioDefinition? Find(string scenarioId)
        => All.FirstOrDefault(s => string.Equals(s.Id, scenarioId, StringComparison.OrdinalIgnoreCase));
}
