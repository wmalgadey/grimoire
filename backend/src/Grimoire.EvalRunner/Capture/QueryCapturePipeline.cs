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
/// The Query capture tier (T099, 008-query-agent) — mirrors <see cref="IngestCapturePipeline"/>
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
    private readonly int _maxParallelSamples;

    /// <param name="maxParallelSamples">
    /// How many of the scenario's samples are captured concurrently (see
    /// <see cref="CaptureParallelism"/>). A sample's own turns stay strictly sequential —
    /// they are one conversation, each turn's prompt built from the previous answers — so
    /// the concurrency is across samples only. Defaults to sequential capture; the
    /// composition root passes what <c>--parallel</c> resolved to.
    /// </param>
    public QueryCapturePipeline(
        RecordingStore store,
        EvalPaths paths,
        QueryAgentProcessInvoker invoker,
        ILogger logger,
        int maxParallelSamples = CaptureParallelism.Sequential)
    {
        _store = store;
        _paths = paths;
        _invoker = invoker;
        _logger = logger;
        _maxParallelSamples = maxParallelSamples;
    }

    public async Task<QueryCaptureScenarioResult> RunScenarioAsync(
        QueryScenarioDefinition scenario,
        ProviderConfiguration provider,
        int requestedSampleCount,
        CancellationToken cancellationToken)
    {
        var providerLabel = EvalObservability.ProviderLabel(provider.Kind);
        var fixtureWikiRoot = _paths.FixtureWikiRoot(scenario.FixtureName);
        var recordings = new List<RecordedSample>();
        var sampleResults = new List<QueryCaptureSampleResult>();
        string? model = null;

        var slots = new QuerySampleSlot[requestedSampleCount];
        await Parallel.ForAsync(
            0,
            requestedSampleCount,
            CaptureParallelism.Options(_maxParallelSamples, cancellationToken),
            async (i, sampleToken) => slots[i] = await CaptureSampleAsync(
                scenario, provider, providerLabel, fixtureWikiRoot, i + 1, sampleToken));

        // Ordered assembly: samples complete in provider-response order, recordings are
        // addressed by the encoded sample/turn number.
        foreach (var slot in slots)
        {
            sampleResults.Add(slot.Result);
            recordings.AddRange(slot.Recordings);
            model ??= slot.Model;
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

    /// <summary>
    /// One logical sample — its whole turn sequence — sharing nothing with its siblings: its
    /// own sandbox copy of the fixture wiki, its own write-lock directory, its own scratch
    /// capture files and its own spawned agent per turn. The turns inside run strictly in
    /// order (each carries the previous answers as prior turns); only whole samples are
    /// captured concurrently, which is what makes <see cref="CaptureParallelism"/> safe here.
    /// </summary>
    private async Task<QuerySampleSlot> CaptureSampleAsync(
        QueryScenarioDefinition scenario,
        ProviderConfiguration provider,
        string providerLabel,
        string fixtureWikiRoot,
        int sampleNumber,
        CancellationToken cancellationToken)
    {
        var turnSequence = scenario.ResolveTurnSequence(sampleNumber - 1);
        var recordings = new List<RecordedSample>();
        QueryCaptureSampleResult? sampleResult = null;
        string? model = null;
        var priorTurns = new List<(string Prompt, string Answer)>();
        string lastTurnId = "-";
        var allTurnsCaptured = true;
        string? failureDetail = null;

        // ADR-015 (012-query-synthesis-writes): each logical sample gets its own
        // sandbox copy of the fixture wiki + write-locks dir, not the fixture directly
        // — Query can now write, so sharing the on-disk fixture across samples would
        // let one sample's created page collide with (or leak into) the next.
        using var sandbox = QueryEvalSandbox.Create(fixtureWikiRoot, $"capture-{scenario.Id}-{sampleNumber:00}");
        var wikiRoot = sandbox.WikiRoot;

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
                    AgentModelMode.Capture(capturePath, provider), CaptureTurnBudget, sandbox.WriteLocksDir, cancellationToken);

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
                    var score = QueryDeterministicScorers.Score(
                        scenario.ScorerId, new QuerySampleRunData(run.Answer ?? string.Empty, run.CreatedPages, wikiRoot));
                    outcome = new RecordedOutcome("completed", score.Checks);
                    sampleResult = new QueryCaptureSampleResult(sampleNumber, turnId, Captured: true, score.Pass, Detail: null);
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
            return QuerySampleSlot.Failed(
                new QueryCaptureSampleResult(sampleNumber, lastTurnId, Captured: false, Pass: null, failureDetail));
        }

        return new QuerySampleSlot(
            sampleResult ?? new QueryCaptureSampleResult(
                sampleNumber, lastTurnId, Captured: false, Pass: null, "The sample produced no final turn."),
            recordings,
            model);
    }

    /// <summary>One sample's ordered outcome: its result row, the recordings its turns
    /// produced, and the model they reported.</summary>
    private readonly record struct QuerySampleSlot(
        QueryCaptureSampleResult Result, IReadOnlyList<RecordedSample> Recordings, string? Model)
    {
        public static QuerySampleSlot Failed(QueryCaptureSampleResult result) => new(result, [], Model: null);
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300];
}
