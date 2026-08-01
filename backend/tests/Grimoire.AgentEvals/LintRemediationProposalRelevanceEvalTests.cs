using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Replay;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentEvals;

/// <summary>
/// T028 (015-lint-board-parity, US3, SC-006) — the agent-behavior evaluation gating
/// SC-006: "≥ 90% of sampled agent-proposed remediation action tasks are judged by a
/// reviewing user to be a relevant, actionable response to the finding that produced
/// them". Mirrors <see cref="LintReplayEvalTests"/>'s always-running replay-tier shape
/// exactly (same ADR-012 recorded-replay harness, same trust-then-threshold assertion
/// pattern) — the only difference is the scenario/scorer pair
/// (<see cref="LintScenarioDefinitions.RemediationProposalsRelevant"/>,
/// <c>lint-remediation-proposals-relevant</c>) and that the golden set it scores against
/// (<see cref="Grimoire.EvalRunner.Scoring.RemediationGoldenSet"/>) stands in for live
/// human review (see that class's doc comment for the caveat).
///
/// <b>No trusted recording exists yet.</b> This scenario has never been captured against
/// live model output — there is no `data/evals/recordings/lint-remediation-proposals/`
/// directory, so <see cref="LintStalenessCheck.Evaluate"/> returns
/// <see cref="TrustStatus.Missing"/> and this test fails with a message naming the exact
/// capture command, exactly like every other Lint/Query scenario would before its first
/// capture (same shape <see cref="LintReplayEvalTests"/>'s four scenarios went through in
/// 013-lint-agent T046). This is the expected, honest state of an eval whose fixtures
/// require a live LLM call this environment cannot make (Constitution II: recorded
/// evidence must come from a real captured run, never fabricated) — T028 is left
/// unchecked in tasks.md until a maintainer with provider credentials runs:
///
/// <code>
/// dotnet run --project backend/src/Grimoire.EvalRunner -- capture --scenario lint-remediation-proposals
/// </code>
///
/// (needs `GRIMOIRE_EVAL_PROVIDER_API_KEY`/`ANTHROPIC_AUTH_TOKEN` set per the existing
/// eval-capture workflow used for 013's `lint-*` scenarios and 008/012's `query-*`
/// scenarios.) Once captured, this test starts asserting the ≥ 90% threshold like its
/// siblings — no code change needed here.
/// </summary>
[Collection("EvalRunnerProcessTests")]
public class LintRemediationProposalRelevanceEvalTests
{
    [Fact]
    public Task SC006_ProposedRemediationTasks_ReplayAtThreshold()
        => AssertScenarioAsync(LintScenarioDefinitions.RemediationProposalsRelevant);

    private static async Task AssertScenarioAsync(LintScenarioDefinition scenario)
    {
        var paths = EvalPaths.Discover();
        var store = new RecordingStore(paths.DefaultRecordingsRoot);
        var pipeline = new LintReplayPipeline(store, paths, LintAgentProcessInvoker.ForRepo(paths), NullLogger.Instance);

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
