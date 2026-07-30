using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Scoring;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging;

namespace Grimoire.EvalRunner.Replay;

/// <summary>One replayed sample's outcome.</summary>
public sealed record LintReplaySampleResult(
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

/// <summary>One Lint scenario's aggregated replay outcome against its spec-defined threshold.</summary>
public sealed record LintScenarioReplayResult(
    string ScenarioId,
    TrustStatus TrustStatus,
    double Threshold,
    double SuccessRate,
    bool ThresholdMet,
    string? Model,
    DateTimeOffset? CapturedAt,
    IReadOnlyList<LintReplaySampleResult> Samples,
    string? Detail)
{
    public bool IsTrustedPass => TrustStatus == TrustStatus.Trusted && ThresholdMet;
}

/// <summary>
/// The Lint replay tier (013-lint-agent, deferred from T017/T018/T032/T033 to this
/// Phase 6 task per their deviation notes) — mirrors <see cref="QueryReplayPipeline"/>
/// for Lint's no-per-run-input, single-turn shape: per sample, replays one spawned Lint
/// agent process against a fresh sandbox copy of the scenario's fixture wiki, exactly
/// mirroring capture and production. Requires no provider configuration and makes zero
/// network calls.
/// </summary>
public sealed class LintReplayPipeline
{
    private static readonly TimeSpan ReplaySampleBudget = TimeSpan.FromSeconds(120);

    private readonly RecordingStore _store;
    private readonly EvalPaths _paths;
    private readonly LintAgentProcessInvoker _invoker;
    private readonly ILogger _logger;

    public LintReplayPipeline(RecordingStore store, EvalPaths paths, LintAgentProcessInvoker invoker, ILogger logger)
    {
        _store = store;
        _paths = paths;
        _invoker = invoker;
        _logger = logger;
    }

    public async Task<LintScenarioReplayResult> RunScenarioAsync(LintScenarioDefinition scenario, CancellationToken cancellationToken)
    {
        var trust = LintStalenessCheck.Evaluate(scenario, _store, _paths);
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

            return new LintScenarioReplayResult(
                scenario.Id, trust.Status, scenario.Threshold, SuccessRate: 0, ThresholdMet: false,
                trust.Manifest?.Model, trust.Manifest?.CapturedAt, Samples: [], trust.Detail);
        }

        var manifest = trust.Manifest!;
        var results = new List<LintReplaySampleResult>();
        for (var i = 0; i < manifest.Samples.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ReplaySampleAsync(scenario, manifest, i + 1, cancellationToken));
        }

        var successes = results.Count(r => r.Pass == true);
        var rate = results.Count == 0 ? 0 : (double)successes / results.Count;
        var anyUntrusted = results.Any(r => r.TrustStatus != TrustStatus.Trusted);

        return new LintScenarioReplayResult(
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

    private async Task<LintReplaySampleResult> ReplaySampleAsync(
        LintScenarioDefinition scenario,
        RecordingManifest manifest,
        int sampleNumber,
        CancellationToken cancellationToken)
    {
        var fileName = $"sample-{sampleNumber:00}.json";
        var entry = manifest.Samples.FirstOrDefault(s => s.File == fileName);
        if (entry is null)
        {
            return Finish(new LintReplaySampleResult(
                scenario.Id, sampleNumber, RunId: "-", TrustStatus.Missing, manifest.Model, manifest.CapturedAt, RecordingPath: null,
                Pass: null, Checks: null,
                Detail: $"Recording file '{fileName}' (sample {sampleNumber}) is missing from the manifest. " +
                    $"Re-capture with: {LintStalenessCheck.RefreshCommand(scenario.Id)}"),
                scenario.Id, sampleNumber, manifest);
        }

        var recordingPath = _store.SamplePath(scenario.Id, entry.File);
        if (!File.Exists(recordingPath))
        {
            return Finish(new LintReplaySampleResult(
                scenario.Id, sampleNumber, entry.TaskId, TrustStatus.Missing, manifest.Model, manifest.CapturedAt, recordingPath,
                Pass: null, Checks: null,
                Detail: $"Recording file '{entry.File}' is missing. Capture it with: {LintStalenessCheck.RefreshCommand(scenario.Id)}"),
                scenario.Id, sampleNumber, manifest);
        }

        var actualHash = RecordingStore.ComputeFileSha256(recordingPath);
        if (!string.Equals(actualHash, entry.Sha256, StringComparison.Ordinal))
        {
            return Finish(new LintReplaySampleResult(
                scenario.Id, sampleNumber, entry.TaskId, TrustStatus.Mismatch, manifest.Model, manifest.CapturedAt, recordingPath,
                Pass: null, Checks: null,
                Detail: $"Recording file '{entry.File}' does not match its manifest hash — recordings must be captured, " +
                    $"never hand-edited (FR-004). Re-capture with: {LintStalenessCheck.RefreshCommand(scenario.Id)}"),
                scenario.Id, sampleNumber, manifest);
        }

        var fixtureWikiRoot = _paths.FixtureWikiRoot(scenario.FixtureName);
        using var sandbox = QueryEvalSandbox.Create(fixtureWikiRoot, $"lint-replay-{scenario.Id}-{sampleNumber:00}");
        var wikiRoot = sandbox.WikiRoot;

        var run = await _invoker.RunAsync(
            entry.TaskId, wikiRoot, _paths, AgentModelMode.Replay(recordingPath), ReplaySampleBudget, sandbox.WriteLocksDir,
            cancellationToken);

        var mismatched = run.FailureReason?.Contains("replay_mismatch", StringComparison.Ordinal) == true
            || run.StdErr.Contains("replay_mismatch", StringComparison.Ordinal);
        if (mismatched || !run.Completed)
        {
            return Finish(new LintReplaySampleResult(
                scenario.Id, sampleNumber, entry.TaskId, TrustStatus.Mismatch, manifest.Model, manifest.CapturedAt, recordingPath,
                Pass: null, Checks: null,
                Detail: run.TimedOut
                    ? $"Replay run exceeded its {ReplaySampleBudget.TotalSeconds:0}s budget."
                    : $"Replay diverged from the recording (exit {run.ExitCode}): {Truncate(run.FailureReason ?? run.StdErr)} " +
                        $"Re-capture with: {LintStalenessCheck.RefreshCommand(scenario.Id)}"),
                scenario.Id, sampleNumber, manifest);
        }

        var score = LintDeterministicScorers.Score(
            scenario.ScorerId, new LintSampleRunData(run.Narrative ?? string.Empty, wikiRoot));

        return Finish(new LintReplaySampleResult(
            scenario.Id, sampleNumber, entry.TaskId, TrustStatus.Trusted, manifest.Model, manifest.CapturedAt, recordingPath,
            score.Pass, score.Checks, Detail: null),
            scenario.Id, sampleNumber, manifest);
    }

    private LintReplaySampleResult Finish(LintReplaySampleResult result, string scenarioId, int sampleNumber, RecordingManifest manifest)
    {
        EvalRunnerTelemetry.RecordReplayResult(
            _logger, result.RunId, scenarioId, sampleNumber,
            result.TrustStatus.ToString().ToLowerInvariant(), manifest.Model, manifest.CapturedAt.ToString("O"));
        return result;
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300];
}
