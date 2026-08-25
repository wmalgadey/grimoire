using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Replay;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentEvals;

/// <summary>
/// T043 (013-lint-agent Phase 6 completeness audit) — the always-running Lint replay
/// eval tier, mirroring <see cref="QueryReplayEvalTests"/> for Query and
/// <c>IngestReplayEvalTests</c> for Ingest: one fact per scenario, replaying the
/// versioned genuine recordings under `data/evals/recordings/` through the real
/// Grimoire.LintAgent executable and asserting the spec-defined thresholds (SC-005
/// through SC-008). No provider, no credential, no skip.
///
/// This file closes a real completeness-audit gap: T017/T018/T032/T033 built the
/// scenario/scorer/fixture surface but explicitly deferred the capture/replay
/// infrastructure itself to Phase 6 (see their deviation notes) — without this test,
/// the newly captured recordings (T046) would have no permanent enforcement in the
/// standard PR pipeline, silently losing CI coverage for every agent-judgment success
/// criterion this feature defines (Constitution Principle II/III).
/// </summary>
[Trait("Tier", "SlowEval")]
[Collection("EvalRunnerReplayScenarios")]
public class LintReplayEvalTests
{
    /// <summary>
    /// SC-011 (026-guarded-tool-surface T066): the survey-at-scale scenario — the agent
    /// completes a real survey of a wiki several times its declared context budget without
    /// exceeding that budget. Gates the same way every other scenario here does, so the
    /// standard PR pipeline enforces it.
    /// </summary>
    [Fact]
    public Task SC011_AtScaleSurvey_ReplaysAtThreshold()
        => AssertScenarioAsync(LintScenarioDefinitions.AtScaleSurvey);

    private static async Task AssertScenarioAsync(LintScenarioDefinition scenario)
    {
        var paths = EvalPaths.Discover();
        var store = new RecordingStore(paths.RecordingsRoot);
        var pipeline = new LintReplayPipeline(store, paths, LintAgentProcessInvoker.ForRepo(paths), NullLogger.Instance);

        var result = await pipeline.RunScenarioAsync(scenario, CancellationToken.None);

        // Trust failures (missing/stale/mismatch) are infrastructure outcomes with their
        // own actionable message — deliberately distinct from a judgment/threshold failure.
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
