using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Replay;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentEvals;

/// <summary>
/// T039 (015-lint-board-parity, US4, FR-018) — the agent-behavior evaluation gating
/// FR-018: "the agent chooses apply vs. not-applicable correctly in ≥ 90% of sampled
/// runs" for the remediation-execution mode's re-verification judgment (T035/T036).
/// Mirrors <see cref="LintReplayEvalTests"/>/<see cref="LintRemediationProposalRelevanceEvalTests"/>'s
/// always-running replay-tier shape exactly (same ADR-012 recorded-replay harness, same
/// trust-then-threshold assertion pattern) — the scenario pair
/// (<see cref="RemediationReVerificationScenarioDefinitions.StillApplicable"/>/
/// <see cref="RemediationReVerificationScenarioDefinitions.NoLongerApplicable"/>) and
/// scorer (<see cref="Grimoire.EvalRunner.Scoring.RemediationReVerificationScorer"/>) are
/// new for this task, unlike T028's reuse of an existing lint-run scenario: FR-018
/// exercises a genuinely new invocation mode (T035's <c>--mode remediation-execution</c>),
/// so this task also had to add the mode's own capture/replay pipeline pair
/// (<see cref="RemediationReVerificationReplayPipeline"/>,
/// <c>Grimoire.EvalRunner.Capture.RemediationReVerificationCapturePipeline</c>) and
/// process invoker (<c>LintAgentProcessInvoker.RunRemediationExecutionAsync</c>) — see
/// <c>RemediationReVerificationScorerTests</c> for the scorer's hermetic (no-LLM)
/// coverage, which is fully exercised regardless of live credentials.
///
/// <b>No trusted recording exists yet for either scenario.</b> Neither has ever been
/// captured against live model output — there is no
/// `data/evals/recordings/remediation-reverify-still-applicable/` or
/// `.../remediation-reverify-no-longer-applicable/` directory, so
/// <see cref="RemediationReVerificationStalenessCheck.Evaluate"/> returns
/// <see cref="TrustStatus.Missing"/> and both tests below fail with a message naming the
/// exact capture command, exactly like every other Lint/Query/remediation scenario would
/// before its first capture (same shape as T028's identical situation, 015-lint-board-
/// parity). This is the expected, honest state of an eval whose fixtures require a live
/// LLM call this environment cannot make (Constitution II: recorded evidence must come
/// from a real captured run, never fabricated) — T039 is left unchecked in tasks.md until
/// a maintainer with provider credentials runs:
///
/// <code>
/// dotnet run --project backend/src/Grimoire.EvalRunner -- capture --scenario remediation-reverify-still-applicable --scenario remediation-reverify-no-longer-applicable
/// </code>
///
/// (needs `GRIMOIRE_EVAL_PROVIDER_API_KEY`/`ANTHROPIC_AUTH_TOKEN` set per the existing
/// eval-capture workflow used for 013's `lint-*` scenarios and 008/012's `query-*`
/// scenarios.) Once captured, these tests start asserting the ≥ 90% threshold like their
/// siblings — no code change needed here.
/// </summary>
[Collection("EvalRunnerProcessTests")]
public class RemediationReVerificationEvalTests
{
    [Fact]
    public Task FR018_StillApplicableProposal_AgentAppliesTheFix_ReplayAtThreshold()
        => AssertScenarioAsync(RemediationReVerificationScenarioDefinitions.StillApplicable);

    [Fact]
    public Task FR018_NoLongerApplicableProposal_AgentReportsNotApplicable_ReplayAtThreshold()
        => AssertScenarioAsync(RemediationReVerificationScenarioDefinitions.NoLongerApplicable);

    private static async Task AssertScenarioAsync(RemediationReVerificationScenarioDefinition scenario)
    {
        var paths = EvalPaths.Discover();
        var store = new RecordingStore(paths.DefaultRecordingsRoot);
        var pipeline = new RemediationReVerificationReplayPipeline(store, paths, LintAgentProcessInvoker.ForRepo(paths), NullLogger.Instance);

        var result = await pipeline.RunScenarioAsync(scenario, CancellationToken.None);

        // Trust failures (missing/stale/mismatch) are infrastructure outcomes with their
        // own actionable message — deliberately distinct from a judgment/threshold
        // failure (mirrors LintReplayEvalTests.AssertScenarioAsync exactly).
        Assert.True(
            result.TrustStatus == TrustStatus.Trusted,
            $"Scenario '{scenario.Id}' has no trusted recordings ({result.TrustStatus}): {result.Detail}\n"
            + string.Join("\n", result.Samples
                .Where(s => s.TrustStatus != TrustStatus.Trusted)
                .Select(s => $"  sample {s.Sample}: {s.TrustStatus} — {s.Detail}")));

        Assert.True(
            result.ThresholdMet,
            $"Scenario '{scenario.Id}' threshold not met on recorded evidence: "
            + $"{result.SuccessRate:P1} < {result.Threshold:P0} (model {result.Model}, captured {result.CapturedAt:yyyy-MM-dd}).");
    }
}
