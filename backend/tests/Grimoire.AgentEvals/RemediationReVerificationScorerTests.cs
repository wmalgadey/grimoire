using Grimoire.EvalRunner.Scoring;

namespace Grimoire.AgentEvals;

/// <summary>
/// T039 (015-lint-board-parity, US4, FR-018) hermetic coverage for
/// <see cref="RemediationReVerificationScorer"/> — pure regression insurance for the
/// scoring mechanism itself (no agent involved, no live capture), same spirit as
/// <see cref="LintDeterministicScorersTests"/>. The scenario/fixture/replay-pipeline
/// wiring this scorer feeds is otherwise unexercised until a maintainer with live LLM
/// credentials captures the scenario — see
/// <c>RemediationReVerificationEvalTests</c>'s class doc comment for the exact command
/// and the honest "no trusted recording exists yet" status this environment reports.
/// </summary>
[Trait("Tier", "Fast")]
public class RemediationReVerificationScorerTests
{
    [Fact]
    public void StillApplicableFixture_AgentAppliedTheFix_Passes()
    {
        // Absent `remediationOutcome` on a completed event means "applied" per contract
        // (contracts/remediation-lifecycle-events.md) — the scorer must normalize it.
        var run = new RemediationReVerificationSampleRunData(RemediationOutcome: null, Reason: null);

        var score = RemediationReVerificationScorer.Score(expectedOutcome: "applied", run);

        Assert.True(score.Pass);
        Assert.True(score.Checks["outcome_matches_fixture"]);
        Assert.True(score.Checks["reason_present_when_required"]);
    }

    [Fact]
    public void StillApplicableFixture_AgentWronglyReportedNotApplicable_Fails()
    {
        var run = new RemediationReVerificationSampleRunData(
            RemediationOutcome: "not_applicable", Reason: "Looked fine to me.");

        var score = RemediationReVerificationScorer.Score(expectedOutcome: "applied", run);

        Assert.False(score.Pass);
        Assert.False(score.Checks["outcome_matches_fixture"]);
    }

    [Fact]
    public void NoLongerApplicableFixture_AgentReportedNotApplicableWithReason_Passes()
    {
        var run = new RemediationReVerificationSampleRunData(
            RemediationOutcome: "not_applicable",
            Reason: "The page already had tags; someone else fixed it first.");

        var score = RemediationReVerificationScorer.Score(expectedOutcome: "not_applicable", run);

        Assert.True(score.Pass);
        Assert.True(score.Checks["outcome_matches_fixture"]);
        Assert.True(score.Checks["reason_present_when_required"]);
    }

    [Fact]
    public void NoLongerApplicableFixture_AgentWronglyAppliedAnyway_Fails()
    {
        var run = new RemediationReVerificationSampleRunData(RemediationOutcome: null, Reason: null);

        var score = RemediationReVerificationScorer.Score(expectedOutcome: "not_applicable", run);

        Assert.False(score.Pass);
        Assert.False(score.Checks["outcome_matches_fixture"]);
    }

    [Fact]
    public void NotApplicableOutcome_WithoutAReason_FailsEvenIfTheOutcomeItselfMatches()
    {
        // FR-018/SC-007: a not_applicable outcome without a genuine reason is a reporting
        // defect the scorer must catch independently of whether the outcome matched.
        var run = new RemediationReVerificationSampleRunData(RemediationOutcome: "not_applicable", Reason: "   ");

        var score = RemediationReVerificationScorer.Score(expectedOutcome: "not_applicable", run);

        Assert.False(score.Pass);
        Assert.True(score.Checks["outcome_matches_fixture"]);
        Assert.False(score.Checks["reason_present_when_required"]);
    }
}
