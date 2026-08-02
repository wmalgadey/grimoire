using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Scoring;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging;

namespace Grimoire.EvalRunner.Replay;

/// <summary>One replayed remediation-execution sample's outcome.</summary>
public sealed record RemediationReVerificationReplaySampleResult(
    string ScenarioId,
    int Sample,
    string RunId,
    TrustStatus TrustStatus,
    string? Model,
    DateTimeOffset? CapturedAt,
    string? RecordingPath,
    bool? Pass,
    IReadOnlyDictionary<string, bool>? Checks,
    string? Detail);

/// <summary>One remediation-execution scenario's aggregated replay outcome against its FR-018 threshold.</summary>
public sealed record RemediationReVerificationScenarioReplayResult(
    string ScenarioId,
    TrustStatus TrustStatus,
    double Threshold,
    double SuccessRate,
    bool ThresholdMet,
    string? Model,
    DateTimeOffset? CapturedAt,
    IReadOnlyList<RemediationReVerificationReplaySampleResult> Samples,
    string? Detail)
{
    public bool IsTrustedPass => TrustStatus == TrustStatus.Trusted && ThresholdMet;
}

/// <summary>
/// The remediation-execution re-verification replay tier (T039, 015-lint-board-parity,
/// FR-018) — mirrors <see cref="LintReplayPipeline"/> exactly for the sibling invocation
/// mode: per sample, replays one spawned remediation-execution agent process against a
/// fresh sandbox copy of the scenario's fixture wiki, exactly mirroring capture and
/// production. Requires no provider configuration and makes zero network calls.
/// </summary>
public sealed class RemediationReVerificationReplayPipeline
{
    private static readonly TimeSpan ReplaySampleBudget = TimeSpan.FromSeconds(120);

    private readonly RecordingStore _store;
    private readonly EvalPaths _paths;
    private readonly LintAgentProcessInvoker _invoker;
    private readonly ILogger _logger;

    public RemediationReVerificationReplayPipeline(
        RecordingStore store, EvalPaths paths, LintAgentProcessInvoker invoker, ILogger logger)
    {
        _store = store;
        _paths = paths;
        _invoker = invoker;
        _logger = logger;
    }

    public async Task<RemediationReVerificationScenarioReplayResult> RunScenarioAsync(
        RemediationReVerificationScenarioDefinition scenario, CancellationToken cancellationToken)
    {
        var trust = RemediationReVerificationStalenessCheck.Evaluate(scenario, _store, _paths);
        if (trust.Status != TrustStatus.Trusted)
        {
            if (trust.Status == TrustStatus.Stale)
            {
                EvalRunnerTelemetry.RecordRecordingStale(
                    _logger, scenario.Id, trust.ChangedFingerprints, _store.ScenarioDirectory(scenario.Id));
            }
            else
            {
                EvalRunnerTelemetry.RecordReplayResult(
                    _logger, taskId: "-", scenario.Id, sample: 0,
                    trust.Status.ToString().ToLowerInvariant(), trust.Manifest?.Model, trust.Manifest?.CapturedAt.ToString("O"));
            }

            return new RemediationReVerificationScenarioReplayResult(
                scenario.Id, trust.Status, scenario.Threshold, SuccessRate: 0, ThresholdMet: false,
                trust.Manifest?.Model, trust.Manifest?.CapturedAt, Samples: [], trust.Detail);
        }

        var manifest = trust.Manifest!;
        var results = new List<RemediationReVerificationReplaySampleResult>();
        for (var i = 0; i < manifest.Samples.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ReplaySampleAsync(scenario, manifest, i + 1, cancellationToken));
        }

        var successes = results.Count(r => r.Pass == true);
        var rate = results.Count == 0 ? 0 : (double)successes / results.Count;
        var anyUntrusted = results.Any(r => r.TrustStatus != TrustStatus.Trusted);

        return new RemediationReVerificationScenarioReplayResult(
            scenario.Id,
            anyUntrusted ? TrustStatus.Mismatch : TrustStatus.Trusted,
            scenario.Threshold,
            rate,
            ThresholdMet: rate >= scenario.Threshold,
            manifest.Model,
            manifest.CapturedAt,
            results,
            Detail: anyUntrusted
                ? "One or more samples failed to replay faithfully — see per-sample details."
                : null);
    }

    private async Task<RemediationReVerificationReplaySampleResult> ReplaySampleAsync(
        RemediationReVerificationScenarioDefinition scenario,
        RecordingManifest manifest,
        int sampleNumber,
        CancellationToken cancellationToken)
    {
        var fileName = $"sample-{sampleNumber:00}.json";
        var entry = manifest.Samples.FirstOrDefault(s => s.File == fileName);
        if (entry is null)
        {
            return Finish(new RemediationReVerificationReplaySampleResult(
                scenario.Id, sampleNumber, RunId: "-", TrustStatus.Missing, manifest.Model, manifest.CapturedAt, RecordingPath: null,
                Pass: null, Checks: null,
                Detail: $"Recording file '{fileName}' (sample {sampleNumber}) is missing from the manifest. " +
                    $"Re-capture with: {RemediationReVerificationStalenessCheck.RefreshCommand(scenario.Id)}"),
                scenario.Id, sampleNumber, manifest);
        }

        var recordingPath = _store.SamplePath(scenario.Id, entry.File);
        if (!File.Exists(recordingPath))
        {
            return Finish(new RemediationReVerificationReplaySampleResult(
                scenario.Id, sampleNumber, entry.TaskId, TrustStatus.Missing, manifest.Model, manifest.CapturedAt, recordingPath,
                Pass: null, Checks: null,
                Detail: $"Recording file '{entry.File}' is missing. Capture it with: " +
                    $"{RemediationReVerificationStalenessCheck.RefreshCommand(scenario.Id)}"),
                scenario.Id, sampleNumber, manifest);
        }

        var actualHash = RecordingStore.ComputeFileSha256(recordingPath);
        if (!string.Equals(actualHash, entry.Sha256, StringComparison.Ordinal))
        {
            return Finish(new RemediationReVerificationReplaySampleResult(
                scenario.Id, sampleNumber, entry.TaskId, TrustStatus.Mismatch, manifest.Model, manifest.CapturedAt, recordingPath,
                Pass: null, Checks: null,
                Detail: $"Recording file '{entry.File}' does not match its manifest hash — recordings must be captured, " +
                    $"never hand-edited (FR-004). Re-capture with: {RemediationReVerificationStalenessCheck.RefreshCommand(scenario.Id)}"),
                scenario.Id, sampleNumber, manifest);
        }

        var fixtureWikiRoot = _paths.FixtureWikiRoot(scenario.FixtureName);
        using var sandbox = QueryEvalSandbox.Create(fixtureWikiRoot, $"remediation-reverify-replay-{scenario.Id}-{sampleNumber:00}");
        var wikiRoot = sandbox.WikiRoot;

        var run = await _invoker.RunRemediationExecutionAsync(
            entry.TaskId, entry.TaskId, wikiRoot, _paths, AgentModelMode.Replay(recordingPath), ReplaySampleBudget,
            sandbox.WriteLocksDir, scenario.ProposalTitle, scenario.ProposalDescription, scenario.ProposalTargetPath,
            cancellationToken);

        var mismatched = run.FailureReason?.Contains("replay_mismatch", StringComparison.Ordinal) == true
            || run.StdErr.Contains("replay_mismatch", StringComparison.Ordinal);
        if (mismatched || !run.Completed)
        {
            return Finish(new RemediationReVerificationReplaySampleResult(
                scenario.Id, sampleNumber, entry.TaskId, TrustStatus.Mismatch, manifest.Model, manifest.CapturedAt, recordingPath,
                Pass: null, Checks: null,
                Detail: run.TimedOut
                    ? $"Replay run exceeded its {ReplaySampleBudget.TotalSeconds:0}s budget."
                    : $"Replay diverged from the recording (exit {run.ExitCode}): {Truncate(run.FailureReason ?? run.StdErr)} " +
                        $"Re-capture with: {RemediationReVerificationStalenessCheck.RefreshCommand(scenario.Id)}"),
                scenario.Id, sampleNumber, manifest);
        }

        var score = RemediationReVerificationScorer.Score(
            scenario.ExpectedOutcome, new RemediationReVerificationSampleRunData(run.RemediationOutcome, run.Reason));

        return Finish(new RemediationReVerificationReplaySampleResult(
            scenario.Id, sampleNumber, entry.TaskId, TrustStatus.Trusted, manifest.Model, manifest.CapturedAt, recordingPath,
            score.Pass, score.Checks, Detail: null),
            scenario.Id, sampleNumber, manifest);
    }

    private RemediationReVerificationReplaySampleResult Finish(
        RemediationReVerificationReplaySampleResult result, string scenarioId, int sampleNumber, RecordingManifest manifest)
    {
        EvalRunnerTelemetry.RecordReplayResult(
            _logger, result.RunId, scenarioId, sampleNumber,
            result.TrustStatus.ToString().ToLowerInvariant(), manifest.Model, manifest.CapturedAt.ToString("O"));
        return result;
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300];
}
