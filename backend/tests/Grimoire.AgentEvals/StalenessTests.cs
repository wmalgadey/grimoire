using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Replay;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentEvals;

/// <summary>
/// T035 — staleness detection (spec 009 US3 / FR-008, SC-005): fingerprint drift flags
/// exactly the affected scenarios, never counts as a trusted pass, and names the changed
/// fingerprint kinds plus the refresh command. Operates on a copied fake repo root so
/// the real instruction files are never mutated.
/// </summary>
[Collection("EvalRunnerProcessTests")]
public class StalenessTests : IDisposable
{
    private readonly string _fakeRepoRoot;
    private readonly EvalPaths _paths;
    private readonly string _recordingsRoot;
    private readonly RecordingStore _store;

    public StalenessTests()
    {
        // Fake repo root: copies of the instruction files and fixtures, so drift can be
        // introduced freely without touching the repository.
        _fakeRepoRoot = Path.Combine(Path.GetTempPath(), "grimoire-staleness", Guid.NewGuid().ToString("N"));
        var real = EvalPaths.Discover();

        CopyDirectory(real.AgentInstructionsDir, Path.Combine(_fakeRepoRoot, "data", "agents", "ingest"));
        CopyDirectory(real.FixturesRoot, Path.Combine(_fakeRepoRoot, "backend", "tests", "Grimoire.AgentEvals", "Fixtures"));
        Directory.CreateDirectory(Path.Combine(_fakeRepoRoot, ".specify"));

        _paths = new EvalPaths(_fakeRepoRoot);
        _recordingsRoot = Path.Combine(_fakeRepoRoot, "data", "evals", "recordings");
        _store = new RecordingStore(_recordingsRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_fakeRepoRoot, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public void InstructionFileDrift_FlagsScenarioStale_NamingFingerprintAndRefreshCommand()
    {
        SyntheticRecordings.WriteScenario(_store, ScenarioDefinitions.ConventionAdherence, _paths, sampleCount: 1);

        File.AppendAllText(_paths.SystemPromptPath, "\n- drift probe\n");

        var report = StalenessCheck.Evaluate(ScenarioDefinitions.ConventionAdherence, _store, _paths);

        Assert.Equal(TrustStatus.Stale, report.Status);
        Assert.Contains("system_prompt", report.ChangedFingerprints);
        Assert.Contains("capture --scenario convention-adherence", report.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void FixtureDrift_FlagsOnlyTheAffectedScenario()
    {
        SyntheticRecordings.WriteScenario(_store, ScenarioDefinitions.ConventionAdherence, _paths, sampleCount: 1);
        SyntheticRecordings.WriteScenario(_store, ScenarioDefinitions.UpdateOverDuplicate, _paths, sampleCount: 1);

        // overlapping-topic is UpdateOverDuplicate's fixture; ConventionAdherence uses empty-topic.
        File.AppendAllText(
            Path.Combine(_paths.FixtureWikiRoot("overlapping-topic"), "pages", "retrieval-patterns.md"),
            "\ndrift probe\n");

        var affected = StalenessCheck.Evaluate(ScenarioDefinitions.UpdateOverDuplicate, _store, _paths);
        var unaffected = StalenessCheck.Evaluate(ScenarioDefinitions.ConventionAdherence, _store, _paths);

        Assert.Equal(TrustStatus.Stale, affected.Status);
        Assert.Contains("fixture", affected.ChangedFingerprints);
        Assert.Equal(TrustStatus.Trusted, unaffected.Status);
    }

    [Fact]
    public async Task StaleScenario_NeverReplaysAsTrustedPass_AndSkipsAgentSpawns()
    {
        SyntheticRecordings.WriteScenario(_store, ScenarioDefinitions.ConventionAdherence, _paths, sampleCount: 1);
        File.AppendAllText(_paths.SystemPromptPath, "\n- drift probe\n");

        // The invoker resolves the agent from the REAL repo build output; the stale path
        // never spawns it, which is part of what this test asserts.
        var pipeline = new ReplayPipeline(_store, _paths, AgentProcessInvoker.ForRepo(EvalPaths.Discover()), NullLogger.Instance);
        var result = await pipeline.RunScenarioAsync(ScenarioDefinitions.ConventionAdherence, CancellationToken.None);

        Assert.Equal(TrustStatus.Stale, result.TrustStatus);
        Assert.False(result.IsTrustedPass);
        Assert.Empty(result.Samples);
        Assert.Contains("capture --scenario convention-adherence", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void JudgePromptFingerprint_TracksOnlyJudgeScoredScenarios()
    {
        var steeringFingerprints = StalenessCheck.CurrentFingerprints(ScenarioDefinitions.SteeringAdoption, _paths);
        var deterministicFingerprints = StalenessCheck.CurrentFingerprints(ScenarioDefinitions.ConventionAdherence, _paths);

        Assert.True(steeringFingerprints.ContainsKey(Fingerprints.JudgePromptKey));
        Assert.False(deterministicFingerprints.ContainsKey(Fingerprints.JudgePromptKey));
    }

    [Fact]
    public void ScenarioDefinitionDrift_ChangesTheScenarioDefinitionFingerprint()
    {
        var baseline = ScenarioDefinitions.ConventionAdherence;
        var drifted = baseline with { Threshold = 0.42 };

        var baselineFingerprints = StalenessCheck.CurrentFingerprints(baseline, _paths);
        var driftedFingerprints = StalenessCheck.CurrentFingerprints(drifted, _paths);

        Assert.NotEqual(
            baselineFingerprints[Fingerprints.ScenarioDefinitionKey],
            driftedFingerprints[Fingerprints.ScenarioDefinitionKey]);
        Assert.Equal(
            baselineFingerprints[Fingerprints.SystemPromptKey],
            driftedFingerprints[Fingerprints.SystemPromptKey]);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }
    }
}
