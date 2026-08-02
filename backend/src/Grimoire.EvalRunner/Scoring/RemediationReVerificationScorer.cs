namespace Grimoire.EvalRunner.Scoring;

/// <summary>
/// The remediation-execution mode's terminal-event outcome, as reported by the run
/// (T039, 015-lint-board-parity, FR-018). Mirrors <see cref="LintSampleRunData"/>'s role
/// for the lint-run scorers — everything a scorer may inspect about one sample.
/// </summary>
public sealed record RemediationReVerificationSampleRunData(string? RemediationOutcome, string? Reason);

/// <summary>
/// T039 (015-lint-board-parity, FR-018): the deterministic scorer for the
/// remediation-execution mode's re-verification eval. Unlike
/// <see cref="LintDeterministicScorers.RemediationProposalsRelevant"/> (T028), which
/// stands in for subjective human review via a frozen golden set (research.md R6's
/// caveat), this scorer needs no such stand-in: each fixture's
/// <see cref="Scenarios.RemediationReVerificationScenarioDefinition.ExpectedOutcome"/> is
/// an objective fact the fixture authors control directly — "does the wiki state this
/// scenario replays against still contain the problem the proposal describes" is
/// something the fixture's author knows for certain, not a matter of taste. Scoring is
/// therefore a straight equality check against what the agent actually reported on its
/// terminal event (contracts/remediation-lifecycle-events.md `remediationOutcome`),
/// never a judgment made here (Constitution Principle V — the judgment stays entirely in
/// data/agents/lint/system-prompt.md's "Remediation Execution Mode" section, T036).
/// </summary>
public static class RemediationReVerificationScorer
{
    public const string ScorerId = "remediation-reverification-outcome-matches-fixture";

    public static SampleScore Score(string expectedOutcome, RemediationReVerificationSampleRunData run)
    {
        // Absent `remediationOutcome` on a `completed` event means "applied" per the
        // contract (contracts/remediation-lifecycle-events.md) — normalize before
        // comparing, mirroring RemediationRunCoordinator's own Hub-side mapping.
        var actualOutcome = string.IsNullOrWhiteSpace(run.RemediationOutcome)
            ? "applied"
            : run.RemediationOutcome;

        var outcomeMatches = string.Equals(actualOutcome, expectedOutcome, StringComparison.Ordinal);

        // FR-018/SC-007: a `not_applicable` outcome must always carry a genuine reason —
        // an empty one is a reporting defect even when the outcome itself is correct.
        var reasonPresentWhenRequired = expectedOutcome != "not_applicable"
            || !string.IsNullOrWhiteSpace(run.Reason);

        var checks = new Dictionary<string, bool>
        {
            ["outcome_matches_fixture"] = outcomeMatches,
            ["reason_present_when_required"] = reasonPresentWhenRequired,
        };

        return new SampleScore(outcomeMatches && reasonPresentWhenRequired, false, checks);
    }
}
