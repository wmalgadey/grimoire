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
[Trait("Tier", "SlowEval")]
[Collection("EvalRunnerReplayScenarios")]
public class IngestReplayEvalTests
{
    [Fact]
    public Task SC006_UpdateOverDuplicate_ReplaysAtThreshold()
        => AssertScenarioAsync(IngestScenarioDefinitions.UpdateOverDuplicate);

    [Fact]
    public Task SC007_ConventionAdherence_ReplaysAtThreshold()
        => AssertScenarioAsync(IngestScenarioDefinitions.ConventionAdherence);

    [Fact]
    public Task SC008_CatalogDiscoverability_ReplaysAtThreshold()
        => AssertScenarioAsync(IngestScenarioDefinitions.CatalogDiscoverability);

    [Fact]
    public Task SC009_InstructionChangeAdoption_ReplaysAtThreshold()
        => AssertScenarioAsync(IngestScenarioDefinitions.InstructionChangeAdoption);

    [Fact]
    public Task SC010_AdversarialSource_ReplaysAtThreshold_WithNoOutOfScopeWrites()
        => AssertScenarioAsync(IngestScenarioDefinitions.AdversarialSource);

    [Fact]
    public Task SC007_SteeringAdoption_ReplaysAtThreshold()
        => AssertScenarioAsync(IngestScenarioDefinitions.SteeringAdoption);

    [Fact]
    public Task SC005_LogParagraphSpecificity_ReplaysAtThreshold()
        => AssertScenarioAsync(IngestScenarioDefinitions.LogParagraphSpecificity);

    [Fact]
    public Task SC007_CatalogDescriptionSpecificity_ReplaysAtThreshold()
        => AssertScenarioAsync(IngestScenarioDefinitions.CatalogDescriptionSpecificity);

    /// <summary>
    /// T049 (022-align-wiki-structure, US2, SC-009): asserts, against recorded evidence,
    /// that a created article's category is never one of the four reserved harness
    /// surfaces. This scenario has no recording yet as of this task (022-align-wiki-structure's
    /// T050 captures it, or reports capture as blocked) — until then this fact fails with
    /// an actionable "no trusted recordings" message, exactly the FR-016 merge-gate
    /// behavior the replay tier is designed to enforce for a new/changed scenario.
    /// </summary>
    [Fact]
    public Task SC009_ReservedSurfaceAvoidance_ReplaysAtThreshold()
        => AssertScenarioAsync(IngestScenarioDefinitions.ReservedSurfaceAvoidance);

    private static async Task AssertScenarioAsync(ScenarioDefinition scenario)
    {
        var paths = EvalPaths.Discover();
        var store = new RecordingStore(paths.RecordingsRoot);
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
