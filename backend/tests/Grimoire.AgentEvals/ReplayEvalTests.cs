using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Replay;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentEvals;

/// <summary>
/// T019 — the always-running replay eval tier (spec 009 US1): one fact per scenario,
/// replaying the versioned genuine recordings under `data/evals/recordings/` through the
/// real agent executable and asserting the unchanged spec thresholds. No provider, no
/// credential, no skip. A missing/stale/mismatched recording fails with the actionable
/// refresh command — in the PR pipeline that failure IS the FR-016 merge gate for
/// instruction-file changes.
/// </summary>
[Collection("EvalRunnerProcessTests")]
public class ReplayEvalTests
{
    [Fact]
    public Task SC006_UpdateOverDuplicate_ReplaysAtThreshold()
        => AssertScenarioAsync(ScenarioDefinitions.UpdateOverDuplicate);

    [Fact]
    public Task SC007_ConventionAdherence_ReplaysAtThreshold()
        => AssertScenarioAsync(ScenarioDefinitions.ConventionAdherence);

    [Fact]
    public Task SC008_CatalogDiscoverability_ReplaysAtThreshold()
        => AssertScenarioAsync(ScenarioDefinitions.CatalogDiscoverability);

    [Fact]
    public Task SC009_InstructionChangeAdoption_ReplaysAtThreshold()
        => AssertScenarioAsync(ScenarioDefinitions.InstructionChangeAdoption);

    [Fact]
    public Task SC010_AdversarialSource_ReplaysAtThreshold_WithNoOutOfScopeWrites()
        => AssertScenarioAsync(ScenarioDefinitions.AdversarialSource);

    [Fact]
    public Task SC007_SteeringAdoption_ReplaysAtThreshold()
        => AssertScenarioAsync(ScenarioDefinitions.SteeringAdoption);

    private static async Task AssertScenarioAsync(ScenarioDefinition scenario)
    {
        var paths = EvalPaths.Discover();
        var store = new RecordingStore(paths.DefaultRecordingsRoot);
        var pipeline = new ReplayPipeline(store, paths, AgentProcessInvoker.ForRepo(paths), NullLogger.Instance);

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
            result.NoOutOfScopeGuaranteeHeld,
            $"Scenario '{scenario.Id}': an out-of-scope write succeeded in at least one recorded run (SC-010 guarantee).");

        Assert.True(
            result.ThresholdMet,
            $"Scenario '{scenario.Id}' threshold not met on recorded evidence: "
            + $"{result.SuccessRate:P1} < {result.Threshold:P0} (model {result.Model}, captured {result.CapturedAt:yyyy-MM-dd}).");
    }
}
