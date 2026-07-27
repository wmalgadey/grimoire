using Grimoire.AgentRuntime.Core.Adapters.Replay;
using Grimoire.EvalRunner.Providers;
using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Scoring;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging;

namespace Grimoire.EvalRunner.Capture;

/// <summary>One logical sample's capture-run outcome (may span more than one spawned
/// agent process turn — only the final turn's answer is scored, per T098).</summary>
public sealed record QueryCaptureSampleResult(
    int Sample,
    string TurnId,
    bool Captured,
    bool? Pass,
    string? Detail);

/// <summary>One Query scenario's capture-run outcome.</summary>
public sealed record QueryCaptureScenarioResult(
    string ScenarioId,
    string? Model,
    double Threshold,
    double SuccessRate,
    bool ThresholdMet,
    bool Stored,
    IReadOnlyList<QueryCaptureSampleResult> Samples,
    string? Detail);

/// <summary>
/// The Query capture tier (T099, 008-query-agent) — mirrors <see cref="CapturePipeline"/>
/// for the Query agent's conversational, artifact-free shape: per logical sample, spawns
/// one real Query agent process PER conversation turn (matching production's
/// one-spawn-per-Query-Turn model), feeding each turn's prompt/answer forward as the next
/// turn's prior-turn context, then a wholesale atomic replacement of the scenario's
/// recording set. Partial scenarios never reach the store.
/// </summary>
public sealed class QueryCapturePipeline
{
    private static readonly TimeSpan CaptureTurnBudget = TimeSpan.FromMinutes(20);

    private readonly RecordingStore _store;
    private readonly EvalPaths _paths;
    private readonly QueryAgentProcessInvoker _invoker;
    private readonly ILogger _logger;

    public QueryCapturePipeline(RecordingStore store, EvalPaths paths, QueryAgentProcessInvoker invoker, ILogger logger)
    {
        _store = store;
        _paths = paths;
        _invoker = invoker;
        _logger = logger;
    }

    public async Task<QueryCaptureScenarioResult> RunScenarioAsync(
        QueryScenarioDefinition scenario,
        ProviderConfiguration provider,
        int requestedSampleCount,
        CancellationToken cancellationToken)
    {
        var providerLabel = EvalObservability.ProviderLabel(provider.Kind);
        var wikiRoot = _paths.FixtureWikiRoot(scenario.FixtureName);
        var recordings = new List<RecordedSample>();
        var sampleResults = new List<QueryCaptureSampleResult>();
        string? model = null;

        for (var i = 0; i < requestedSampleCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sampleNumber = i + 1;
            var turnSequence = scenario.ResolveTurnSequence(i);
            var priorTurns = new List<(string Prompt, string Answer)>();
            string lastTurnId = "-";
            var allTurnsCaptured = true;
            string? failureDetail = null;

            for (var turnPosition = 1; turnPosition <= turnSequence.Count; turnPosition++)
            {
                var prompt = turnSequence[turnPosition - 1];
                var turnId = $"capture-{scenario.Id}-{sampleNumber:00}-t{turnPosition}-{Guid.NewGuid():N}";
                lastTurnId = turnId;
                var recordedSampleNumber = QuerySampleNumbering.Encode(sampleNumber, turnPosition);

                var captureScratch = Path.Combine(Path.GetTempPath(), "grimoire-eval-runner", "capture-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(captureScratch);
                var capturePath = Path.Combine(captureScratch, $"sample-{recordedSampleNumber:00}.json");

                try
                {
                    using var span = EvalRunnerTelemetry.StartCaptureRun(turnId, scenario.Id, providerLabel, provider.Model);

                    var run = await _invoker.RunAsync(
                        turnId, wikiRoot, prompt, priorTurns, _paths,
                        AgentModelMode.Capture(capturePath, provider), CaptureTurnBudget, cancellationToken);

                    if (run.TimedOut)
                    {
                        EvalObservability.RecordSampleTimeout(
                            _logger, $"{scenario.Id}-{sampleNumber}-t{turnPosition}", providerLabel, provider.Model, CaptureTurnBudget.TotalSeconds);
                        allTurnsCaptured = false;
                        failureDetail = $"Sample {sampleNumber} turn {turnPosition} exceeded the {CaptureTurnBudget.TotalMinutes:0}min capture budget.";
                        break;
                    }

                    if (!run.Completed || !File.Exists(capturePath))
                    {
                        allTurnsCaptured = false;
                        failureDetail = $"Sample {sampleNumber} turn {turnPosition} produced no captured turn " +
                            $"(exit {run.ExitCode}, completed={run.Completed}): {Truncate(run.StdErr)}";
                        break;
                    }

                    var rawCapture = RecordingSerialization.Load(capturePath);
                    model ??= rawCapture.Model;

                    RecordedOutcome? outcome = null;
                    var isFinalTurn = turnPosition == turnSequence.Count;
                    if (isFinalTurn)
                    {
                        var score = QueryDeterministicScorers.Score(scenario.ScorerId, new QuerySampleRunData(run.Answer ?? string.Empty));
                        outcome = new RecordedOutcome("completed", score.Checks);
                        sampleResults.Add(new QueryCaptureSampleResult(sampleNumber, turnId, Captured: true, score.Pass, Detail: null));
                    }

                    recordings.Add(rawCapture with
                    {
                        Sample = recordedSampleNumber,
                        TaskId = turnId,
                        Outcome = outcome,
                    });

                    EvalRunnerTelemetry.RecordRecordingCaptured(
                        _logger, turnId, scenario.Id, recordedSampleNumber, rawCapture.Model,
                        _store.SamplePath(scenario.Id, $"sample-{recordedSampleNumber:00}.json"), providerLabel);

                    priorTurns.Add((prompt, run.Answer ?? string.Empty));
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(captureScratch))
                        {
                            Directory.Delete(captureScratch, recursive: true);
                        }
                    }
                    catch
                    {
                        // Best-effort scratch cleanup.
                    }
                }
            }

            if (!allTurnsCaptured)
            {
                sampleResults.Add(new QueryCaptureSampleResult(sampleNumber, lastTurnId, Captured: false, Pass: null, failureDetail));
            }
        }

        var allCaptured = sampleResults.Count == requestedSampleCount && sampleResults.All(r => r.Captured);
        var successes = sampleResults.Count(r => r.Pass == true);
        var rate = sampleResults.Count == 0 ? 0 : (double)successes / sampleResults.Count;

        if (!allCaptured)
        {
            return new QueryCaptureScenarioResult(
                scenario.Id, model, scenario.Threshold, rate, rate >= scenario.Threshold, Stored: false, sampleResults,
                Detail: "Not every sample produced a recording — the scenario's recording set was NOT replaced (no partial stores).");
        }

        var fingerprints = QueryStalenessCheck.CurrentFingerprints(scenario, _paths);
        _store.ReplaceScenario(scenario.Id, capturedAt: DateTimeOffset.UtcNow, model: model ?? "unknown", providerLabel, fingerprints, recordings);

        return new QueryCaptureScenarioResult(
            scenario.Id, model, scenario.Threshold, rate, rate >= scenario.Threshold, Stored: true, sampleResults, Detail: null);
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300];
}
