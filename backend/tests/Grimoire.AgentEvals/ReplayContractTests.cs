using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Replay;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentEvals;

/// <summary>
/// T021 — hermetic replay-contract tests over synthetic recordings (harness mechanics,
/// never scenario evidence): zero-config execution (SC-001), determinism (SC-004),
/// provenance (SC-003/FR-012), missing recording (FR-009), tampered recording and
/// conversation divergence (FR-010/FR-004). Spawns the real agent executable with the
/// replay adapter — no provider, no network.
/// </summary>
[Trait("Tier", "Fast")]
public class ReplayContractTests : IDisposable
{
    // SC-001: replay must work with zero provider configuration. Expressed as an
    // injected empty environment rather than by nulling process-wide variables (#121):
    // every env read on the replay path goes through this function, and the spawned
    // child gets no provider variable regardless, because AgentProcessInvoker scrubs
    // them from the child environment by construction.
    private static readonly Func<string, string?> EmptyEnvironment = _ => null;

    private readonly string _recordingsRoot;
    private readonly EvalPaths _paths;
    private readonly RecordingStore _store;
    private readonly ReplayPipeline _pipeline;

    public ReplayContractTests()
    {
        _recordingsRoot = Path.Combine(Path.GetTempPath(), "grimoire-replay-contract", Guid.NewGuid().ToString("N"));
        _paths = EvalPaths.Discover();
        _store = new RecordingStore(_recordingsRoot, EmptyEnvironment);
        _pipeline = new ReplayPipeline(_store, _paths, AgentProcessInvoker.ForRepo(_paths, EmptyEnvironment), NullLogger.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_recordingsRoot, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public async Task Replay_SyntheticRecording_ZeroConfig_ExecutesTrusted_WithProvenance()
    {
        SyntheticRecordings.WriteScenario(_store, IngestScenarioDefinitions.ConventionAdherence, _paths, sampleCount: 1);

        var result = await _pipeline.RunScenarioAsync(IngestScenarioDefinitions.ConventionAdherence, CancellationToken.None);

        Assert.Equal(TrustStatus.Trusted, result.TrustStatus);
        var sample = Assert.Single(result.Samples);
        Assert.Equal(TrustStatus.Trusted, sample.TrustStatus);

        // SC-003 / FR-012: every result names its provenance.
        Assert.Equal(SyntheticRecordings.Model, sample.Model);
        Assert.NotNull(sample.CapturedAt);
        Assert.NotNull(sample.RecordingPath);
        Assert.True(File.Exists(sample.RecordingPath));

        // The synthetic single-turn run produces no wiki pages, so the scorer honestly
        // reports a non-pass — trust and scoring are independent axes.
        Assert.False(sample.Pass);
    }

    [Fact]
    public async Task Replay_SameRecordingTwice_ProducesIdenticalOutcomes()
    {
        SyntheticRecordings.WriteScenario(_store, IngestScenarioDefinitions.ConventionAdherence, _paths, sampleCount: 1);

        var first = await _pipeline.RunScenarioAsync(IngestScenarioDefinitions.ConventionAdherence, CancellationToken.None);
        var second = await _pipeline.RunScenarioAsync(IngestScenarioDefinitions.ConventionAdherence, CancellationToken.None);

        Assert.Equal(first.TrustStatus, second.TrustStatus);
        Assert.Equal(first.SuccessRate, second.SuccessRate);
        Assert.Equal(first.ThresholdMet, second.ThresholdMet);
        Assert.Equal(
            first.Samples.Select(s => (s.Sample, s.TrustStatus, s.Pass)),
            second.Samples.Select(s => (s.Sample, s.TrustStatus, s.Pass)));
    }

    [Fact]
    public async Task Replay_MissingScenario_ReportsMissingWithCaptureCommand()
    {
        var result = await _pipeline.RunScenarioAsync(IngestScenarioDefinitions.UpdateOverDuplicate, CancellationToken.None);

        Assert.Equal(TrustStatus.Missing, result.TrustStatus);
        Assert.NotNull(result.Detail);
        Assert.Contains("capture --scenario update-over-duplicate", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_MissingSampleFile_ReportsMissingWithCaptureCommand()
    {
        SyntheticRecordings.WriteScenario(_store, IngestScenarioDefinitions.ConventionAdherence, _paths, sampleCount: 1);
        File.Delete(_store.SamplePath("convention-adherence", "sample-01.json"));

        var result = await _pipeline.RunScenarioAsync(IngestScenarioDefinitions.ConventionAdherence, CancellationToken.None);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(TrustStatus.Missing, sample.TrustStatus);
        Assert.Contains("capture --scenario convention-adherence", sample.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_TamperedSampleFile_ReportsMismatch_NotJudgment()
    {
        SyntheticRecordings.WriteScenario(_store, IngestScenarioDefinitions.ConventionAdherence, _paths, sampleCount: 1);
        var samplePath = _store.SamplePath("convention-adherence", "sample-01.json");
        File.AppendAllText(samplePath, " ");

        var result = await _pipeline.RunScenarioAsync(IngestScenarioDefinitions.ConventionAdherence, CancellationToken.None);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(TrustStatus.Mismatch, sample.TrustStatus);
        Assert.Null(sample.Pass);
        Assert.Contains("manifest hash", sample.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_DivergentRecordedConversation_ReportsReplayMismatch()
    {
        SyntheticRecordings.WriteScenario(
            _store, IngestScenarioDefinitions.ConventionAdherence, _paths, sampleCount: 1, divergentConversation: true);

        var result = await _pipeline.RunScenarioAsync(IngestScenarioDefinitions.ConventionAdherence, CancellationToken.None);

        var sample = Assert.Single(result.Samples);
        Assert.Equal(TrustStatus.Mismatch, sample.TrustStatus);
        Assert.Null(sample.Pass);
        Assert.Contains("diverged", sample.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
