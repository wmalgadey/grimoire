namespace Grimoire.EvalRunner.Scenarios;

/// <summary>
/// One evaluated remediation-execution scenario (T039, 015-lint-board-parity, FR-018).
/// Unlike <see cref="LintScenarioDefinition"/> (whole-wiki input, no per-run steering),
/// a remediation-execution run's input is one specific authorized proposal — the same
/// three fields <c>RemediationExecutionCliOptions</c> carries
/// (<c>backend/src/Grimoire.LintAgent/RemediationExecutionCliOptions.cs</c>) — replayed
/// against a fixture wiki whose content the fixture author has deliberately set to make
/// exactly one outcome correct (<see cref="ExpectedOutcome"/>): either the proposal's
/// premise still holds (<c>applied</c>) or the fixture content has already moved past it
/// (<c>not_applicable</c>). This objectivity (research.md R6's golden-set caveat does not
/// apply here — see <c>RemediationReVerificationScorer</c>'s doc comment) is what makes
/// FR-018 hermetically scoreable without a human-adjudicated stand-in.
/// </summary>
public sealed record RemediationReVerificationScenarioDefinition(
    string Id,
    string FixtureName,
    string ProposalTitle,
    string ProposalDescription,
    string? ProposalTargetPath,
    string ExpectedOutcome,
    double Threshold,
    string ScorerId)
{
    /// <summary>Stable serialization for the `scenario_definition` staleness fingerprint (mirrors <see cref="LintScenarioDefinition.StableSerialization"/>).</summary>
    public string StableSerialization() =>
        $"id={Id}\nfixture={FixtureName}\ntitle={ProposalTitle}\ndescription={ProposalDescription}\n" +
        $"targetPath={ProposalTargetPath}\nexpected={ExpectedOutcome}\nthreshold={Threshold:0.00}\nscorer={ScorerId}\n";
}

/// <summary>
/// The remediation-execution re-verification eval scenarios (FR-018): a matched pair over
/// a shared fixture family (<c>backend/tests/Grimoire.AgentEvals/Fixtures/
/// remediation-reverify-still-applicable/</c> and its <c>-no-longer-applicable</c>
/// sibling) — the same proposal, replayed against wiki content before vs. after the fix
/// it describes was independently applied, exactly the spec's Q&amp;A scenario ("the
/// agent re-verifies at execution time").
/// </summary>
public static class RemediationReVerificationScenarioDefinitions
{
    private const string Title = "Add missing tags to example-topic";
    private const string Description =
        "The page example-topic.md has no tags frontmatter. Add tags: [concept/caching, tech/dotnet].";
    private const string TargetPath = "example-topic.md";

    /// <summary>The proposal's premise still holds — the page is still untagged when re-verified.</summary>
    public static readonly RemediationReVerificationScenarioDefinition StillApplicable = new(
        Id: "remediation-reverify-still-applicable",
        FixtureName: "remediation-reverify-still-applicable",
        ProposalTitle: Title,
        ProposalDescription: Description,
        ProposalTargetPath: TargetPath,
        ExpectedOutcome: "applied",
        Threshold: 0.90,
        ScorerId: Scoring.RemediationReVerificationScorer.ScorerId);

    /// <summary>The page already gained tags after the proposal was written — moot by the time execution runs.</summary>
    public static readonly RemediationReVerificationScenarioDefinition NoLongerApplicable = new(
        Id: "remediation-reverify-no-longer-applicable",
        FixtureName: "remediation-reverify-no-longer-applicable",
        ProposalTitle: Title,
        ProposalDescription: Description,
        ProposalTargetPath: TargetPath,
        ExpectedOutcome: "not_applicable",
        Threshold: 0.90,
        ScorerId: Scoring.RemediationReVerificationScorer.ScorerId);

    public static readonly IReadOnlyList<RemediationReVerificationScenarioDefinition> All =
        [StillApplicable, NoLongerApplicable];

    public static RemediationReVerificationScenarioDefinition? Find(string scenarioId)
        => All.FirstOrDefault(s => string.Equals(s.Id, scenarioId, StringComparison.OrdinalIgnoreCase));
}
