using Grimoire.AgentRuntime.Core.Adapters.Replay;
using Grimoire.EvalRunner.Providers;
using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Scoring;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging;

namespace Grimoire.EvalRunner.Capture;

/// <summary>One sample's capture-run outcome.</summary>
public sealed record LintCaptureSampleResult(
    int Sample,
    string RunId,
    bool Captured,
    bool? Pass,
    string? Detail);

/// <summary>One Lint scenario's capture-run outcome.</summary>
public sealed record LintCaptureScenarioResult(
    string ScenarioId,
    string? Model,
    double Threshold,
    double SuccessRate,
    bool ThresholdMet,
    bool Stored,
    IReadOnlyList<LintCaptureSampleResult> Samples,
    string? Detail);

/// <summary>
/// The Lint capture tier (013-lint-agent, deferred from T017/T018/T032/T033 to this
/// Phase 6 task per their deviation notes) — mirrors <see cref="QueryCapturePipeline"/>
/// for Lint's no-per-run-input, single-turn shape: per sample, a fresh sandbox copy of
/// the scenario's fixture wiki (<see cref="QueryEvalSandbox"/>, reused unchanged — it is
/// already agent-agnostic), one spawned real Lint agent process, live deterministic
/// scoring against the post-run sandbox state, then a wholesale atomic replacement of the
/// scenario's recording set. Partial scenarios never reach the store.
/// </summary>
public sealed class LintCapturePipeline
{
    private static readonly TimeSpan CaptureSampleBudget = TimeSpan.FromMinutes(20);

    private readonly RecordingStore _store;
    private readonly EvalPaths _paths;
    private readonly LintAgentProcessInvoker _invoker;
    private readonly ILogger _logger;
    private readonly int _maxParallelSamples;

    /// <param name="maxParallelSamples">
    /// How many of the scenario's samples are captured concurrently (see
    /// <see cref="CaptureParallelism"/>). Defaults to sequential capture; the composition
    /// root passes what <c>--parallel</c> resolved to.
    /// </param>
    public LintCapturePipeline(
        RecordingStore store,
        EvalPaths paths,
        LintAgentProcessInvoker invoker,
        ILogger logger,
        int maxParallelSamples = CaptureParallelism.Sequential)
    {
        _store = store;
        _paths = paths;
        _invoker = invoker;
        _logger = logger;
        _maxParallelSamples = maxParallelSamples;
    }

    public async Task<LintCaptureScenarioResult> RunScenarioAsync(
        LintScenarioDefinition scenario,
        ProviderConfiguration provider,
        int requestedSampleCount,
        CancellationToken cancellationToken)
    {
        var providerLabel = EvalObservability.ProviderLabel(provider.Kind);
        var fixtureWikiRoot = _paths.FixtureWikiRoot(scenario.FixtureName);
        var recordings = new List<RecordedSample>();
        var sampleResults = new List<LintCaptureSampleResult>();
        string? model = null;

        var slots = new LintSampleSlot[requestedSampleCount];
        await Parallel.ForAsync(
            0,
            requestedSampleCount,
            CaptureParallelism.Options(_maxParallelSamples, cancellationToken),
            async (i, sampleToken) => slots[i] = await CaptureSampleAsync(
                scenario, provider, providerLabel, fixtureWikiRoot, i + 1, sampleToken));

        // Ordered assembly: samples complete in provider-response order, recordings are
        // addressed by sample number.
        foreach (var slot in slots)
        {
            sampleResults.Add(slot.Result);
            if (slot.Recording is not null)
            {
                recordings.Add(slot.Recording);
            }

            model ??= slot.Model;
        }

        var allCaptured = sampleResults.Count == requestedSampleCount && sampleResults.All(r => r.Captured);
        var successes = sampleResults.Count(r => r.Pass == true);
        var rate = sampleResults.Count == 0 ? 0 : (double)successes / sampleResults.Count;

        if (!allCaptured)
        {
            return new LintCaptureScenarioResult(
                scenario.Id, model, scenario.Threshold, rate, rate >= scenario.Threshold, Stored: false, sampleResults,
                Detail: "Not every sample produced a recording — the scenario's recording set was NOT replaced (no partial stores).");
        }

        var fingerprints = LintStalenessCheck.CurrentFingerprints(scenario, _paths);
        _store.ReplaceScenario(scenario.Id, capturedAt: DateTimeOffset.UtcNow, model: model ?? "unknown", providerLabel, fingerprints, recordings);

        return new LintCaptureScenarioResult(
            scenario.Id, model, scenario.Threshold, rate, rate >= scenario.Threshold, Stored: true, sampleResults, Detail: null);
    }

    /// <summary>
    /// One sample, sharing nothing with its siblings: its own sandbox copy of the fixture
    /// wiki, its own write-lock directory, its own scratch capture file and its own spawned
    /// Lint agent. That independence is what makes <see cref="CaptureParallelism"/> safe.
    /// </summary>
    private async Task<LintSampleSlot> CaptureSampleAsync(
        LintScenarioDefinition scenario,
        ProviderConfiguration provider,
        string providerLabel,
        string fixtureWikiRoot,
        int sampleNumber,
        CancellationToken cancellationToken)
    {
        var runId = $"capture-{scenario.Id}-{sampleNumber:00}-{Guid.NewGuid():N}";

        using var sandbox = QueryEvalSandbox.Create(fixtureWikiRoot, $"lint-capture-{scenario.Id}-{sampleNumber:00}");
        var wikiRoot = sandbox.WikiRoot;

        var captureScratch = Path.Combine(Path.GetTempPath(), "grimoire-eval-runner", "capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(captureScratch);
        var capturePath = Path.Combine(captureScratch, $"sample-{sampleNumber:00}.json");

        try
        {
            using var span = EvalRunnerTelemetry.StartCaptureRun(runId, scenario.Id, providerLabel, provider.Model);

            var run = await _invoker.RunAsync(
                runId, wikiRoot, _paths, AgentModelMode.Capture(capturePath, provider), CaptureSampleBudget, sandbox.WriteLocksDir,
                cancellationToken);

            if (run.TimedOut)
            {
                EvalObservability.RecordSampleTimeout(
                    _logger, $"{scenario.Id}-{sampleNumber}", providerLabel, provider.Model, CaptureSampleBudget.TotalSeconds);
                return LintSampleSlot.Failed(new LintCaptureSampleResult(
                    sampleNumber, runId, Captured: false, Pass: null,
                    Detail: $"Sample {sampleNumber} exceeded the {CaptureSampleBudget.TotalMinutes:0}min capture budget."));
            }

            if (!run.Completed || !File.Exists(capturePath))
            {
                return LintSampleSlot.Failed(new LintCaptureSampleResult(
                    sampleNumber, runId, Captured: false, Pass: null,
                    Detail: $"Sample {sampleNumber} produced no captured run " +
                        $"(exit {run.ExitCode}, completed={run.Completed}): {Truncate(run.StdErr)}"));
            }

            var rawCapture = RecordingSerialization.Load(capturePath);

            var contentTokensRead = MeasureContentTokensRead(scenario, capturePath, fixtureWikiRoot);

            var score = LintDeterministicScorers.Score(
                scenario.ScorerId,
                new LintSampleRunData(
                    run.Narrative ?? string.Empty,
                    wikiRoot,
                    run.ProposedActions,
                    contentTokensRead,
                    scenario.ContextBudgetTokens));

            var recording = rawCapture with
            {
                Sample = sampleNumber,
                TaskId = runId,
                Outcome = new RecordedOutcome("completed", score.Checks),
            };

            EvalRunnerTelemetry.RecordRecordingCaptured(
                _logger, runId, scenario.Id, sampleNumber, rawCapture.Model,
                _store.SamplePath(scenario.Id, $"sample-{sampleNumber:00}.json"), providerLabel);

            return new LintSampleSlot(
                new LintCaptureSampleResult(sampleNumber, runId, Captured: true, score.Pass, Detail: null),
                recording,
                rawCapture.Model);
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

    /// <summary>One sample's ordered outcome: its result row, the recording it produced (none
    /// when the sample failed), and the model it reported.</summary>
    private readonly record struct LintSampleSlot(LintCaptureSampleResult Result, RecordedSample? Recording, string? Model)
    {
        public static LintSampleSlot Failed(LintCaptureSampleResult result) => new(result, Recording: null, Model: null);
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300];

    // Same read-side accounting the replay pipeline scores against (SC-011), so a scenario's
    // capture-time and replay-time verdicts are computed identically. Measured against the
    // pristine fixture, not the mutated sandbox — see ReadShapeAccounting.
    private static int? MeasureContentTokensRead(LintScenarioDefinition scenario, string capturePath, string fixtureWikiRoot) =>
        scenario.ContextBudgetTokens is null
            ? null
            : ReadShapeAccounting.Measure(capturePath, fixtureWikiRoot).ContentTokens;
}
