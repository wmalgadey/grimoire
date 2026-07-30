using Grimoire.AgentRuntime.Core.Adapters.Replay;
using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Scoring;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging;

namespace Grimoire.EvalRunner.Replay;

/// <summary>One replayed logical sample's outcome — may have replayed more than one
/// spawned agent process turn; <see cref="RecordingPath"/> is the FINAL turn's recording
/// (the one the score was computed from).</summary>
public sealed record QueryReplaySampleResult(
    string ScenarioId,
    int Sample,
    string TaskId,
    TrustStatus TrustStatus,
    string? Model,
    DateTimeOffset? CapturedAt,
    string? RecordingPath,
    bool? Pass,
    IReadOnlyDictionary<string, bool>? Checks,
    string? Detail);

/// <summary>One Query scenario's aggregated replay outcome against its spec-defined threshold.</summary>
public sealed record QueryScenarioReplayResult(
    string ScenarioId,
    TrustStatus TrustStatus,
    double Threshold,
    double SuccessRate,
    bool ThresholdMet,
    string? Model,
    DateTimeOffset? CapturedAt,
    IReadOnlyList<QueryReplaySampleResult> Samples,
    string? Detail)
{
    public bool IsTrustedPass => TrustStatus == TrustStatus.Trusted && ThresholdMet;
}

/// <summary>
/// The Query replay tier (T099, 008-query-agent) — mirrors <see cref="ReplayPipeline"/>
/// for the Query agent's conversational, artifact-free shape: per logical sample,
/// replays one spawned agent process PER conversation turn (each pinned to its own
/// recording file via <see cref="QuerySampleNumbering"/>), feeding each turn's recorded
/// answer forward as the next turn's prior-turn context — exactly mirroring capture and
/// production. Requires no provider configuration and makes zero network calls.
/// </summary>
public sealed class QueryReplayPipeline
{
    private static readonly TimeSpan ReplayTurnBudget = TimeSpan.FromSeconds(120);

    private readonly RecordingStore _store;
    private readonly EvalPaths _paths;
    private readonly QueryAgentProcessInvoker _invoker;
    private readonly ILogger _logger;

    public QueryReplayPipeline(RecordingStore store, EvalPaths paths, QueryAgentProcessInvoker invoker, ILogger logger)
    {
        _store = store;
        _paths = paths;
        _invoker = invoker;
        _logger = logger;
    }

    public async Task<QueryScenarioReplayResult> RunScenarioAsync(QueryScenarioDefinition scenario, CancellationToken cancellationToken)
    {
        var trust = QueryStalenessCheck.Evaluate(scenario, _store, _paths);
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

            return new QueryScenarioReplayResult(
                scenario.Id, trust.Status, scenario.Threshold, SuccessRate: 0, ThresholdMet: false,
                trust.Manifest?.Model, trust.Manifest?.CapturedAt, Samples: [], trust.Detail);
        }

        var manifest = trust.Manifest!;
        // The recorded logical-sample count is derived from the highest encoded sample
        // number present, not manifest.Samples.Count (each logical sample may occupy
        // more than one manifest entry — one per conversation turn).
        var recordedSampleCount = manifest.Samples.Count == 0
            ? 0
            : manifest.Samples
                .Select(s => QuerySampleNumbering.DecodeSampleIndex(int.Parse(Path.GetFileNameWithoutExtension(s.File)["sample-".Length..])))
                .Distinct()
                .Count();

        var results = new List<QueryReplaySampleResult>();
        for (var i = 0; i < recordedSampleCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ReplaySampleAsync(scenario, manifest, i + 1, cancellationToken));
        }

        var successes = results.Count(r => r.Pass == true);
        var rate = results.Count == 0 ? 0 : (double)successes / results.Count;
        var anyUntrusted = results.Any(r => r.TrustStatus != TrustStatus.Trusted);

        return new QueryScenarioReplayResult(
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

    private async Task<QueryReplaySampleResult> ReplaySampleAsync(
        QueryScenarioDefinition scenario,
        RecordingManifest manifest,
        int sampleNumber,
        CancellationToken cancellationToken)
    {
        var turnSequence = scenario.ResolveTurnSequence(sampleNumber - 1);
        var fixtureWikiRoot = _paths.FixtureWikiRoot(scenario.FixtureName);
        // ADR-015 (012-query-synthesis-writes): same per-sample sandbox rationale as
        // QueryCapturePipeline — replay must not run a write-capable scenario directly
        // against the shared on-disk fixture either.
        using var sandbox = QueryEvalSandbox.Create(fixtureWikiRoot, $"replay-{scenario.Id}-{sampleNumber:00}");
        var wikiRoot = sandbox.WikiRoot;
        var priorTurns = new List<(string Prompt, string Answer)>();

        for (var turnPosition = 1; turnPosition <= turnSequence.Count; turnPosition++)
        {
            var recordedSampleNumber = QuerySampleNumbering.Encode(sampleNumber, turnPosition);
            var fileName = $"sample-{recordedSampleNumber:00}.json";
            var entry = manifest.Samples.FirstOrDefault(s => s.File == fileName);
            var isFinalTurn = turnPosition == turnSequence.Count;

            if (entry is null)
            {
                return Finish(new QueryReplaySampleResult(
                    scenario.Id, sampleNumber, TaskId: "-", TrustStatus.Missing, manifest.Model, manifest.CapturedAt, RecordingPath: null,
                    Pass: null, Checks: null,
                    Detail: $"Recording file '{fileName}' (sample {sampleNumber}, turn {turnPosition}) is missing from the manifest. " +
                        $"Re-capture with: {QueryStalenessCheck.RefreshCommand(scenario.Id)}"),
                    scenario.Id, sampleNumber, manifest);
            }

            var recordingPath = _store.SamplePath(scenario.Id, entry.File);
            if (!File.Exists(recordingPath))
            {
                return Finish(new QueryReplaySampleResult(
                    scenario.Id, sampleNumber, entry.TaskId, TrustStatus.Missing, manifest.Model, manifest.CapturedAt, recordingPath,
                    Pass: null, Checks: null,
                    Detail: $"Recording file '{entry.File}' is missing. Capture it with: {QueryStalenessCheck.RefreshCommand(scenario.Id)}"),
                    scenario.Id, sampleNumber, manifest);
            }

            var actualHash = RecordingStore.ComputeFileSha256(recordingPath);
            if (!string.Equals(actualHash, entry.Sha256, StringComparison.Ordinal))
            {
                return Finish(new QueryReplaySampleResult(
                    scenario.Id, sampleNumber, entry.TaskId, TrustStatus.Mismatch, manifest.Model, manifest.CapturedAt, recordingPath,
                    Pass: null, Checks: null,
                    Detail: $"Recording file '{entry.File}' does not match its manifest hash — recordings must be captured, " +
                        $"never hand-edited (FR-004). Re-capture with: {QueryStalenessCheck.RefreshCommand(scenario.Id)}"),
                    scenario.Id, sampleNumber, manifest);
            }

            var prompt = turnSequence[turnPosition - 1];
            var run = await _invoker.RunAsync(
                entry.TaskId, wikiRoot, prompt, priorTurns, _paths,
                AgentModelMode.Replay(recordingPath), ReplayTurnBudget, sandbox.WriteLocksDir, cancellationToken);

            var mismatched = run.FailureReason?.Contains("replay_mismatch", StringComparison.Ordinal) == true
                || run.StdErr.Contains("replay_mismatch", StringComparison.Ordinal);
            if (mismatched || !run.Completed)
            {
                return Finish(new QueryReplaySampleResult(
                    scenario.Id, sampleNumber, entry.TaskId, TrustStatus.Mismatch, manifest.Model, manifest.CapturedAt, recordingPath,
                    Pass: null, Checks: null,
                    Detail: run.TimedOut
                        ? $"Replay run exceeded its {ReplayTurnBudget.TotalSeconds:0}s budget."
                        : $"Replay diverged from the recording (turn {turnPosition}, exit {run.ExitCode}): " +
                            $"{Truncate(run.FailureReason ?? run.StdErr)} " +
                            $"Re-capture with: {QueryStalenessCheck.RefreshCommand(scenario.Id)}"),
                    scenario.Id, sampleNumber, manifest);
            }

            if (!isFinalTurn)
            {
                priorTurns.Add((prompt, run.Answer ?? string.Empty));
                continue;
            }

            var score = QueryDeterministicScorers.Score(
                scenario.ScorerId, new QuerySampleRunData(run.Answer ?? string.Empty, run.CreatedPages, wikiRoot));
            return Finish(new QueryReplaySampleResult(
                scenario.Id, sampleNumber, entry.TaskId, TrustStatus.Trusted, manifest.Model, manifest.CapturedAt, recordingPath,
                score.Pass, score.Checks, Detail: null),
                scenario.Id, sampleNumber, manifest);
        }

        // Unreachable: turnSequence always has at least one turn (validated by scenario construction).
        throw new InvalidOperationException($"Scenario '{scenario.Id}' resolved an empty turn sequence for sample {sampleNumber}.");
    }

    private QueryReplaySampleResult Finish(QueryReplaySampleResult result, string scenarioId, int sampleNumber, RecordingManifest manifest)
    {
        EvalRunnerTelemetry.RecordReplayResult(
            _logger, result.TaskId, scenarioId, sampleNumber,
            result.TrustStatus.ToString().ToLowerInvariant(), manifest.Model, manifest.CapturedAt.ToString("O"));
        return result;
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300];
}
