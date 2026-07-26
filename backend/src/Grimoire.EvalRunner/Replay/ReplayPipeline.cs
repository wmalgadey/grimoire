using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Scoring;
using Grimoire.EvalRunner.Workspace;
using Grimoire.IngestAgent.TaskArtifact;
using Microsoft.Extensions.Logging;

namespace Grimoire.EvalRunner.Replay;

/// <summary>
/// The replay tier (spec 009 US1): per sample — trust check, isolated workspace, the real
/// agent process spawned with <c>GRIMOIRE_MODEL_REPLAY_PATH</c>, unchanged scoring —
/// aggregated against the scenario's spec-defined threshold. Requires no provider
/// configuration by construction and makes zero network calls. This is the same code
/// path the always-running replay eval tests execute.
/// </summary>
public sealed class ReplayPipeline
{
    private static readonly TimeSpan ReplaySampleBudget = TimeSpan.FromSeconds(120);

    private readonly RecordingStore _store;
    private readonly EvalPaths _paths;
    private readonly AgentProcessInvoker _invoker;
    private readonly ILogger _logger;

    public ReplayPipeline(RecordingStore store, EvalPaths paths, AgentProcessInvoker invoker, ILogger logger)
    {
        _store = store;
        _paths = paths;
        _invoker = invoker;
        _logger = logger;
    }

    public async Task<ScenarioReplayResult> RunScenarioAsync(ScenarioDefinition scenario, CancellationToken cancellationToken)
    {
        var trust = StalenessCheck.Evaluate(scenario, _store, _paths);
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

            return new ScenarioReplayResult(
                scenario.Id, trust.Status, scenario.Threshold, SuccessRate: 0, ThresholdMet: false,
                NoOutOfScopeGuaranteeHeld: !scenario.RequiresNoOutOfScopeWrites,
                trust.Manifest?.Model, trust.Manifest?.CapturedAt, Samples: [], trust.Detail);
        }

        var manifest = trust.Manifest!;
        var sampleSpecs = scenario.ResolveSamples(manifest.Samples.Count);
        var results = new List<ReplaySampleResult>();

        for (var i = 0; i < manifest.Samples.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = manifest.Samples[i];
            var spec = i < sampleSpecs.Count ? sampleSpecs[i] : null;
            results.Add(await ReplaySampleAsync(scenario, manifest, entry, i + 1, spec, cancellationToken));
        }

        var successes = results.Count(r => r.Pass == true);
        var rate = results.Count == 0 ? 0 : (double)successes / results.Count;
        var anyUntrusted = results.Any(r => r.TrustStatus != TrustStatus.Trusted);
        var guaranteeHeld = !scenario.RequiresNoOutOfScopeWrites || results.All(r => !r.OutOfScopeWriteSucceeded);

        return new ScenarioReplayResult(
            scenario.Id,
            anyUntrusted ? TrustStatus.Mismatch : TrustStatus.Trusted,
            scenario.Threshold,
            rate,
            ThresholdMet: rate >= scenario.Threshold,
            NoOutOfScopeGuaranteeHeld: guaranteeHeld,
            manifest.Model,
            manifest.CapturedAt,
            results,
            Detail: anyUntrusted
                ? "One or more samples failed to replay faithfully — see per-sample details."
                : null);
    }

    private async Task<ReplaySampleResult> ReplaySampleAsync(
        ScenarioDefinition scenario,
        RecordingManifest manifest,
        ManifestSample entry,
        int sampleNumber,
        SampleSpec? spec,
        CancellationToken cancellationToken)
    {
        var recordingPath = _store.SamplePath(scenario.Id, entry.File);
        // The recorded task id is embedded in the captured conversation's first message,
        // so the replayed run must reuse it verbatim for the conversation to match.
        var taskId = string.IsNullOrWhiteSpace(entry.TaskId)
            ? $"replay-{scenario.Id}-{sampleNumber:00}"
            : entry.TaskId;
        var capturedAt = manifest.CapturedAt;

        using var span = EvalRunnerTelemetry.StartReplayRun(taskId, scenario.Id, entry.File);

        ReplaySampleResult Finish(ReplaySampleResult result)
        {
            span?.SetTag("trust_status", result.TrustStatus.ToString().ToLowerInvariant());
            EvalRunnerTelemetry.RecordReplayResult(
                _logger, taskId, scenario.Id, sampleNumber,
                result.TrustStatus.ToString().ToLowerInvariant(), manifest.Model, capturedAt.ToString("O"));
            return result;
        }

        if (!File.Exists(recordingPath))
        {
            return Finish(new ReplaySampleResult(
                scenario.Id, sampleNumber, taskId, TrustStatus.Missing, manifest.Model, capturedAt, recordingPath,
                Pass: null, OutOfScopeWriteSucceeded: false, Checks: null,
                Detail: $"Recording file '{entry.File}' is missing. Capture it with: {StalenessCheck.RefreshCommand(scenario.Id)}"));
        }

        var actualHash = RecordingStore.ComputeFileSha256(recordingPath);
        if (!string.Equals(actualHash, entry.Sha256, StringComparison.Ordinal))
        {
            return Finish(new ReplaySampleResult(
                scenario.Id, sampleNumber, taskId, TrustStatus.Mismatch, manifest.Model, capturedAt, recordingPath,
                Pass: null, OutOfScopeWriteSucceeded: false, Checks: null,
                Detail: $"Recording file '{entry.File}' does not match its manifest hash — recordings must be " +
                    $"captured, never hand-edited (FR-004). Re-capture with: {StalenessCheck.RefreshCommand(scenario.Id)}"));
        }

        if (spec is null)
        {
            return Finish(new ReplaySampleResult(
                scenario.Id, sampleNumber, taskId, TrustStatus.Mismatch, manifest.Model, capturedAt, recordingPath,
                Pass: null, OutOfScopeWriteSucceeded: false, Checks: null,
                Detail: $"The manifest lists more samples than the scenario defines — definition/recording drift. " +
                    $"Re-capture with: {StalenessCheck.RefreshCommand(scenario.Id)}"));
        }

        using var workspace = EvalWorkspace.Create(
            _paths.FixtureWikiRoot(scenario.FixtureName),
            _paths.AgentInstructionsDir,
            taskId,
            scenario.SystemPromptAppendix);

        var run = await _invoker.RunAsync(
            taskId,
            sourceRef: $"eval://{scenario.Id}/sample-{sampleNumber:00}",
            sourceContent: spec.SourceContent,
            workspace,
            AgentModelMode.Replay(recordingPath),
            spec.UserPrompt,
            ReplaySampleBudget,
            cancellationToken);

        var artifactPath = Path.Combine(workspace.TasksDir, $"{taskId}.md");
        TaskArtifactDocument? artifact = null;
        if (File.Exists(artifactPath))
        {
            artifact = await new TaskArtifactStore().ReadAsync(artifactPath, cancellationToken);
        }

        if (artifact?.FailureReason?.Contains("replay_mismatch", StringComparison.Ordinal) == true
            || (artifact is null && run.StdErr.Contains("replay_mismatch", StringComparison.Ordinal)))
        {
            var reason = artifact?.FailureReason ?? run.StdErr;
            return Finish(new ReplaySampleResult(
                scenario.Id, sampleNumber, taskId, TrustStatus.Mismatch, manifest.Model, capturedAt, recordingPath,
                Pass: null, OutOfScopeWriteSucceeded: false, Checks: null,
                Detail: $"Replay diverged from the recording: {reason} " +
                    $"Re-capture with: {StalenessCheck.RefreshCommand(scenario.Id)}"));
        }

        if (artifact is null)
        {
            return Finish(new ReplaySampleResult(
                scenario.Id, sampleNumber, taskId, TrustStatus.Mismatch, manifest.Model, capturedAt, recordingPath,
                Pass: null, OutOfScopeWriteSucceeded: false, Checks: null,
                Detail: run.TimedOut
                    ? $"Replay run exceeded its {ReplaySampleBudget.TotalSeconds:0}s budget."
                    : $"The agent produced no task artifact (exit {run.ExitCode}): {Truncate(run.StdErr)}"));
        }

        var recordedSample = Grimoire.IngestAgent.AgentCore.Adapters.Replay.RecordingSerialization.Load(recordingPath);
        var judgeVerdict = recordedSample.JudgeVerdicts is { Count: > 0 }
            ? recordedSample.JudgeVerdicts[0].Verdict
            : null;

        var score = DeterministicScorers.Score(scenario, new SampleRunData(
            Status: artifact.Status,
            PageFiles: workspace.PageFiles(),
            IndexContent: workspace.IndexContent(),
            SandboxRoot: workspace.Root,
            PagesTouched: artifact.PagesTouched ?? [],
            JudgeVerdict: judgeVerdict));

        return Finish(new ReplaySampleResult(
            scenario.Id, sampleNumber, taskId, TrustStatus.Trusted, manifest.Model, capturedAt, recordingPath,
            Pass: score.Pass, score.OutOfScopeWriteSucceeded, score.Checks, Detail: null));
    }

    private static string Truncate(string text)
        => text.Length <= 300 ? text : text[..300];
}
