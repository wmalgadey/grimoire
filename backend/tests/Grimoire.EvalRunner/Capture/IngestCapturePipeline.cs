using Grimoire.EvalRunner.Providers;
using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Scoring;
using Grimoire.EvalRunner.Workspace;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Replay;
using Grimoire.IngestAgent.TaskArtifact;
using Microsoft.Extensions.Logging;

namespace Grimoire.EvalRunner.Capture;

/// <summary>One captured sample: its recording plus the live-scored outcome.</summary>
public sealed record CaptureSampleResult(
    int Sample,
    string TaskId,
    bool Captured,
    bool? Pass,
    bool OutOfScopeWriteSucceeded,
    string? Detail);

/// <summary>One scenario's capture-run outcome.</summary>
public sealed record CaptureScenarioResult(
    string ScenarioId,
    string? Model,
    double Threshold,
    double SuccessRate,
    bool ThresholdMet,
    bool NoOutOfScopeGuaranteeHeld,
    bool Stored,
    IReadOnlyList<CaptureSampleResult> Samples,
    string? Detail);

/// <summary>
/// The capture tier (spec 009 US2): per sample — isolated workspace, the real agent
/// spawned with <c>GRIMOIRE_MODEL_CAPTURE_PATH</c> and a scoped provider environment
/// (ADR-004), live scoring, judge invocation for judge-scored scenarios — then a
/// wholesale, atomic replacement of the scenario's recording set with provenance and
/// staleness fingerprints. Partial scenarios never reach the store.
/// </summary>
public sealed class IngestCapturePipeline
{
    private static readonly TimeSpan CaptureSampleBudget = TimeSpan.FromMinutes(20);

    private readonly RecordingStore _store;
    private readonly EvalPaths _paths;
    private readonly IngestAgentProcessInvoker _invoker;
    private readonly ILogger _logger;
    private readonly Func<ProviderConfiguration, IModelClient>? _judgeClientFactory;
    private readonly int _maxParallelSamples;

    /// <param name="maxParallelSamples">
    /// How many of the scenario's samples are captured concurrently (see
    /// <see cref="CaptureParallelism"/>). Defaults to sequential capture; the composition
    /// root passes what <c>--parallel</c> resolved to.
    /// </param>
    public IngestCapturePipeline(
        RecordingStore store,
        EvalPaths paths,
        IngestAgentProcessInvoker invoker,
        ILogger logger,
        Func<ProviderConfiguration, IModelClient>? judgeClientFactory = null,
        int maxParallelSamples = CaptureParallelism.Sequential)
    {
        _maxParallelSamples = maxParallelSamples;
        _store = store;
        _paths = paths;
        _invoker = invoker;
        _logger = logger;
        _judgeClientFactory = judgeClientFactory;
    }

    public async Task<CaptureScenarioResult> RunScenarioAsync(
        ScenarioDefinition scenario,
        ProviderConfiguration provider,
        int requestedSampleCount,
        CancellationToken cancellationToken)
    {
        var providerLabel = EvalObservability.ProviderLabel(provider.Kind);
        var sampleSpecs = scenario.ResolveSamples(requestedSampleCount);
        var recordings = new List<RecordedSample>();
        var sampleResults = new List<CaptureSampleResult>();
        string? model = null;

        var captureScratch = Path.Combine(Path.GetTempPath(), "grimoire-eval-runner", "capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(captureScratch);

        try
        {
            // Ordered slots rather than shared lists: the samples finish in whatever order
            // the provider answers them, but a recording set is ordered by sample number
            // and a replay addresses samples by that number, so the assembly below restores
            // the sequential order regardless of completion order.
            var slots = new SampleSlot[sampleSpecs.Count];
            await Parallel.ForAsync(
                0,
                sampleSpecs.Count,
                CaptureParallelism.Options(_maxParallelSamples, cancellationToken),
                async (i, sampleToken) => slots[i] = await CaptureSampleAsync(
                    scenario, provider, providerLabel, sampleSpecs[i], i + 1, captureScratch, sampleToken));

            foreach (var slot in slots)
            {
                sampleResults.Add(slot.Result);
                if (slot.Recording is not null)
                {
                    recordings.Add(slot.Recording);
                }

                model ??= slot.Model;
            }

            var allCaptured = sampleResults.Count > 0 && sampleResults.All(r => r.Captured);
            var successes = sampleResults.Count(r => r.Pass == true);
            var rate = sampleResults.Count == 0 ? 0 : (double)successes / sampleResults.Count;
            var guaranteeHeld = !scenario.RequiresNoOutOfScopeWrites
                || sampleResults.All(r => !r.OutOfScopeWriteSucceeded);

            if (!allCaptured)
            {
                return new CaptureScenarioResult(
                    scenario.Id, model, scenario.Threshold, rate, rate >= scenario.Threshold, guaranteeHeld,
                    Stored: false, sampleResults,
                    Detail: "Not every sample produced a recording — the scenario's recording set was NOT replaced (no partial stores).");
            }

            var fingerprints = StalenessCheck.CurrentFingerprints(scenario, _paths);
            _store.ReplaceScenario(
                scenario.Id,
                capturedAt: DateTimeOffset.UtcNow,
                model: model ?? "unknown",
                providerKind: providerLabel,
                fingerprints,
                recordings);

            return new CaptureScenarioResult(
                scenario.Id, model, scenario.Threshold, rate, rate >= scenario.Threshold, guaranteeHeld,
                Stored: true, sampleResults, Detail: null);
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

    /// <summary>
    /// Dispatches to the judge-prompt builder for <paramref name="scenario"/>'s
    /// <c>ScorerId</c>. No remaining Ingest scenario is judge-scored (the judge-scored
    /// lower-stakes scenarios were removed per Constitution Principle II, v1.12.0) — the
    /// mechanism stays so a future judge-scored scenario only has to add its mapping here.
    /// </summary>
    private static Task<JudgeVerdict> InvokeJudgeAsync(
        ScenarioDefinition scenario,
        IModelClient judge,
        SampleSpec spec,
        TaskArtifactDocument? artifact,
        EvalWorkspace workspace,
        CancellationToken cancellationToken)
        => scenario.ScorerId switch
        {
            _ => throw new InvalidOperationException(
                $"Scenario '{scenario.Id}' is judge-scored but scorer '{scenario.ScorerId}' has no judge-invocation mapping."),
        };

    /// <summary>
    /// One sample, start to finish, sharing nothing with its siblings: its own workspace
    /// (own wiki copy, own write-lock directory, own tasks dir), its own capture file inside
    /// the scenario's scratch directory, its own spawned agent. That independence is what
    /// makes <see cref="CaptureParallelism"/> safe — this method is the unit that runs
    /// concurrently, and it touches no state of the scenario run around it.
    /// </summary>
    private async Task<SampleSlot> CaptureSampleAsync(
        ScenarioDefinition scenario,
        ProviderConfiguration provider,
        string providerLabel,
        SampleSpec spec,
        int sampleNumber,
        string captureScratch,
        CancellationToken cancellationToken)
    {
        var taskId = $"capture-{scenario.Id}-{sampleNumber:00}-{Guid.NewGuid():N}";
        var capturePath = Path.Combine(captureScratch, $"sample-{sampleNumber:00}.json");

        using var span = EvalRunnerTelemetry.StartCaptureRun(taskId, scenario.Id, providerLabel, provider.Model);
        using var workspace = await EvalWorkspace.CreateAsync(
            _paths.FixtureWikiRoot(scenario.FixtureName),
            _paths.FoundationPromptPath,
            _paths.AgentInstructionsDir,
            taskId,
            scenario.SystemPromptAppendix,
            cancellationToken);

        // The task id and source ref are embedded in the agent's first user
        // message, so replay must reproduce them exactly: the source ref is a
        // stable per-sample value and the task id travels inside the recording.
        var run = await _invoker.RunAsync(
            taskId,
            sourceRef: $"eval://{scenario.Id}/sample-{sampleNumber:00}",
            sourceContent: spec.SourceContent,
            workspace,
            AgentModelMode.Capture(capturePath, provider),
            spec.UserPrompt,
            CaptureSampleBudget,
            cancellationToken);

        if (run.TimedOut)
        {
            EvalObservability.RecordSampleTimeout(
                _logger, $"{scenario.Id}-{sampleNumber}", providerLabel, provider.Model, CaptureSampleBudget.TotalSeconds);
            return SampleSlot.Failed(new CaptureSampleResult(sampleNumber, taskId, Captured: false, Pass: null,
                OutOfScopeWriteSucceeded: false, Detail: $"Sample exceeded the {CaptureSampleBudget.TotalMinutes:0}min capture budget."));
        }

        if (!File.Exists(capturePath))
        {
            return SampleSlot.Failed(new CaptureSampleResult(sampleNumber, taskId, Captured: false, Pass: null,
                OutOfScopeWriteSucceeded: false,
                Detail: $"The agent produced no captured turns (exit {run.ExitCode}): " +
                    $"{Truncate(run.FailureReason ?? run.StdErr)}"));
        }

        var artifactPath = Path.Combine(workspace.TasksDir, $"{taskId}.md");
        TaskArtifactDocument? artifact = File.Exists(artifactPath)
            ? await new TaskArtifactStore().ReadAsync(artifactPath, cancellationToken)
            : null;

        var rawCapture = RecordingSerialization.Load(capturePath);

        List<JudgeVerdict>? judgeVerdicts = null;
        string? judgeVerdictValue = null;
        if (scenario.JudgeScored)
        {
            if (_judgeClientFactory is null)
            {
                throw new InvalidOperationException(
                    $"Scenario '{scenario.Id}' is judge-scored but no judge client factory was provided.");
            }

            var judge = _judgeClientFactory(provider);
            var verdict = await InvokeJudgeAsync(scenario, judge, spec, artifact, workspace, cancellationToken);
            judgeVerdicts = [verdict];
            judgeVerdictValue = verdict.Verdict;
        }

        var score = DeterministicScorers.Score(scenario, new SampleRunData(
            Status: artifact?.Status ?? "failed",
            PageFiles: workspace.PageFiles(),
            IndexContent: workspace.IndexContent(),
            SandboxRoot: workspace.Root,
            PagesTouched: artifact?.PagesTouched ?? [],
            JudgeVerdict: judgeVerdictValue));

        var recording = rawCapture with
        {
            Sample = sampleNumber,
            TaskId = taskId,
            JudgeVerdicts = judgeVerdicts,
            Outcome = new RecordedOutcome(artifact?.Status ?? "failed", score.Checks),
        };

        // Emitted inside the sample's capture span (Principle IV); the path is the
        // recording's final store location after the wholesale swap below.
        EvalRunnerTelemetry.RecordRecordingCaptured(
            _logger,
            taskId,
            scenario.Id,
            sampleNumber,
            rawCapture.Model,
            _store.SamplePath(scenario.Id, $"sample-{sampleNumber:00}.json"),
            providerLabel);

        return new SampleSlot(
            new CaptureSampleResult(
                sampleNumber, taskId, Captured: true, score.Pass, score.OutOfScopeWriteSucceeded, Detail: null),
            recording,
            rawCapture.Model);
    }

    /// <summary>One sample's ordered outcome: its result row, the recording it produced (none
    /// when the sample failed), and the model it reported.</summary>
    private readonly record struct SampleSlot(CaptureSampleResult Result, RecordedSample? Recording, string? Model)
    {
        public static SampleSlot Failed(CaptureSampleResult result) => new(result, Recording: null, Model: null);
    }

    private static string Truncate(string text)
        => text.Length <= 300 ? text : text[..300];
}
