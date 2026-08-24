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
/// <b>T028 closed 2026-08-01</b>: live-captured with `claude-haiku-4-5` (100% success
/// rate against the ≥ 90% threshold) — `data/evals/recordings/lint-remediation-proposals/`
/// is committed and <see cref="LintStalenessCheck.Evaluate"/> returns
/// <see cref="TrustStatus.Trusted"/> against the current `agents/lint/system-prompt.md`
/// fingerprint, so this test asserts the ≥ 90% threshold on that recorded evidence like
/// its siblings in <see cref="LintReplayEvalTests"/>.
///
/// If a future instruction-file change (system prompt, policy) shifts the recorded
/// fingerprint, <see cref="LintStalenessCheck.Evaluate"/> reports
/// <see cref="TrustStatus.Stale"/> instead and this test fails naming the same recapture
/// command a first-time capture would use, exactly like every other Lint/Query scenario
/// (Constitution II: recorded evidence must come from a real captured run, never
/// fabricated — a stale recording is never silently reused):
///
/// <code>
/// dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario lint-remediation-proposals
/// </code>
///
/// (needs `GRIMOIRE_EVAL_PROVIDER_API_KEY`/`ANTHROPIC_AUTH_TOKEN` set per the existing
/// eval-capture workflow used for 013's `lint-*` scenarios and 008/012's `query-*`
/// scenarios.)
/// </summary>
[Trait("Tier", "SlowEval")]
[Collection("EvalRunnerReplayScenarios")]
public class LintRemediationProposalRelevanceEvalTests
{
    [Fact]
    public Task SC006_ProposedRemediationTasks_ReplayAtThreshold()
        => AssertScenarioAsync(LintScenarioDefinitions.RemediationProposalsRelevant);

    private static async Task AssertScenarioAsync(LintScenarioDefinition scenario)
    {
        var paths = EvalPaths.Discover();
        var store = new RecordingStore(paths.RecordingsRoot);
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
