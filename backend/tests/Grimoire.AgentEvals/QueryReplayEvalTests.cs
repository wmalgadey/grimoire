using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Replay;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentEvals;

/// <summary>
/// T100 (008-query-agent) — the always-running Query replay eval tier, mirroring
/// <see cref="IngestReplayEvalTests"/> for Ingest: one fact per scenario, replaying the
/// versioned genuine recordings under `data/evals/recordings/` through the real
/// Grimoire.QueryAgent executable and asserting the unchanged spec thresholds. No
/// provider, no credential, no skip. Supersedes the pre-migration
/// `QueryGroundingEvals`/`QueryFollowUpEvals`/`QueryReadOnlyDeclineEvals` (T047/T061/T070),
/// which always Skipped in the standard CI run (no GRIMOIRE_EVAL=1) and so always failed
/// SC-008's (feature 009) zero-skip gate.
/// </summary>
[Trait("Tier", "SlowEval")]
[Collection("EvalRunnerReplayScenarios")]
public class QueryReplayEvalTests
{
    [Fact]
    public Task SC007_GroundingCovered_ReplaysAtThreshold()
        => AssertScenarioAsync(QueryScenarioDefinitions.GroundingCovered);

    [Fact]
    public Task SC008_GroundingUncovered_ReplaysAtThreshold()
        => AssertScenarioAsync(QueryScenarioDefinitions.GroundingUncovered);

    [Fact]
    public Task SC009_FollowUp_ReplaysAtThreshold()
        => AssertScenarioAsync(QueryScenarioDefinitions.FollowUp);

    [Fact]
    public Task SC010_ReadOnlyDecline_ReplaysAtThreshold()
        => AssertScenarioAsync(QueryScenarioDefinitions.ReadOnlyDecline);

    // 012-query-synthesis-writes (ADR-015): T044 completeness-audit gap fix — the three
    // new Query scenarios below had scorers/recordings but no permanent replay-fact
    // enforcement in the standard PR pipeline, unlike the four scenarios above. Without
    // these, ci.yml's "Run replay agent evals" step would never check SC-005/SC-006/
    // SC-007/SC-008 again after this session, silently losing CI coverage for this
    // feature's agent-judgment thresholds (spec.md Success Criteria; Constitution
    // Principle II/III completeness-audit requirement).
    [Fact]
    public Task SC005_SC007_SynthesisCreated_ReplaysAtThreshold()
        => AssertScenarioAsync(QueryScenarioDefinitions.SynthesisCreated);

    [Fact]
    public Task SC006_SynthesisDeclinedRoutine_ReplaysAtThreshold()
        => AssertScenarioAsync(QueryScenarioDefinitions.SynthesisDeclinedRoutine);

    [Fact]
    public Task SC008_SynthesisDeclineEditRequest_ReplaysAtThreshold()
        => AssertScenarioAsync(QueryScenarioDefinitions.SynthesisDeclineEditRequest);

    private static async Task AssertScenarioAsync(QueryScenarioDefinition scenario)
    {
        var paths = EvalPaths.Discover();
        var store = new RecordingStore(paths.DefaultRecordingsRoot);
        var pipeline = new QueryReplayPipeline(store, paths, QueryAgentProcessInvoker.ForRepo(paths), NullLogger.Instance);

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
