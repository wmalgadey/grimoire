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
public sealed class CapturePipeline
{
    private static readonly TimeSpan CaptureSampleBudget = TimeSpan.FromMinutes(20);

    private readonly RecordingStore _store;
    private readonly EvalPaths _paths;
    private readonly AgentProcessInvoker _invoker;
    private readonly ILogger _logger;
    private readonly Func<ProviderConfiguration, IModelClient>? _judgeClientFactory;

    public CapturePipeline(
        RecordingStore store,
        EvalPaths paths,
        AgentProcessInvoker invoker,
        ILogger logger,
        Func<ProviderConfiguration, IModelClient>? judgeClientFactory = null)
    {
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
            for (var i = 0; i < sampleSpecs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sampleNumber = i + 1;
                var spec = sampleSpecs[i];
                var taskId = $"capture-{scenario.Id}-{sampleNumber:00}-{Guid.NewGuid():N}";
                var capturePath = Path.Combine(captureScratch, $"sample-{sampleNumber:00}.json");

                using var span = EvalRunnerTelemetry.StartCaptureRun(taskId, scenario.Id, providerLabel, provider.Model);
                using var workspace = await EvalWorkspace.CreateAsync(
                    _paths.FixtureWikiRoot(scenario.FixtureName),
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
                    sampleResults.Add(new CaptureSampleResult(sampleNumber, taskId, Captured: false, Pass: null,
                        OutOfScopeWriteSucceeded: false, Detail: $"Sample exceeded the {CaptureSampleBudget.TotalMinutes:0}min capture budget."));
                    continue;
                }

                if (!File.Exists(capturePath))
                {
                    sampleResults.Add(new CaptureSampleResult(sampleNumber, taskId, Captured: false, Pass: null,
                        OutOfScopeWriteSucceeded: false,
                        Detail: $"The agent produced no captured turns (exit {run.ExitCode}): {Truncate(run.StdErr)}"));
                    continue;
                }

                var artifactPath = Path.Combine(workspace.TasksDir, $"{taskId}.md");
                TaskArtifactDocument? artifact = File.Exists(artifactPath)
                    ? await new TaskArtifactStore().ReadAsync(artifactPath, cancellationToken)
                    : null;

                var rawCapture = RecordingSerialization.Load(capturePath);
                model ??= rawCapture.Model;

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

                recordings.Add(rawCapture with
                {
                    Sample = sampleNumber,
                    TaskId = taskId,
                    JudgeVerdicts = judgeVerdicts,
                    Outcome = new RecordedOutcome(artifact?.Status ?? "failed", score.Checks),
                });

                sampleResults.Add(new CaptureSampleResult(
                    sampleNumber, taskId, Captured: true, score.Pass, score.OutOfScopeWriteSucceeded, Detail: null));

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
    /// <c>ScorerId</c> (014-wiki-storage-restructure T061: generalized from the
    /// steering-adoption-only call this method replaces, so SC-005/SC-007's scorers can
    /// share the same capture-time judge-invocation mechanism).
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
            "steering-adoption" => JudgeScoring.JudgeAsync(
                judge, spec.UserPrompt ?? string.Empty, artifact?.Narrative ?? string.Empty,
                workspace.PageFiles(), cancellationToken),
            "log-paragraph-specificity" => InvokeLogParagraphJudgeAsync(judge, workspace, cancellationToken),
            "catalog-description-specificity" => InvokeCatalogDescriptionJudgeAsync(judge, workspace, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Scenario '{scenario.Id}' is judge-scored but scorer '{scenario.ScorerId}' has no judge-invocation mapping."),
        };

    /// <summary>
    /// SC-005: judges the most recently appended <c>log.md</c> entry — the one this
    /// sample's run produced, since every fixture behind this scorer starts with an empty
    /// log.md so the run's own entry is unambiguously the last (and only) heading.
    /// </summary>
    private static Task<JudgeVerdict> InvokeLogParagraphJudgeAsync(
        IModelClient judge, EvalWorkspace workspace, CancellationToken cancellationToken)
    {
        var logContent = workspace.LogContent();
        var headingIndices = LogParagraphSpecificityScorer.FindHeadingLineIndices(logContent);
        var (heading, paragraph) = headingIndices.Count == 0
            ? (string.Empty, string.Empty)
            : LogParagraphSpecificityScorer.ExtractEntry(logContent, headingIndices[^1]);

        return LogParagraphSpecificityScorer.JudgeAsync(judge, heading, paragraph, workspace.PageFiles(), cancellationToken);
    }

    /// <summary>
    /// SC-007: judges the most recently added <c>index.md</c> catalog line — same
    /// last-entry convention as <see cref="InvokeLogParagraphJudgeAsync"/> — against the
    /// actual content of the article it links to.
    /// </summary>
    private static Task<JudgeVerdict> InvokeCatalogDescriptionJudgeAsync(
        IModelClient judge, EvalWorkspace workspace, CancellationToken cancellationToken)
    {
        var indexContent = workspace.IndexContent();
        var lineIndices = CatalogDescriptionSpecificityScorer.FindCatalogLineIndices(indexContent);
        var lines = indexContent.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var (title, path, description, _) = lineIndices.Count == 0
            ? (string.Empty, string.Empty, string.Empty, string.Empty)
            : CatalogDescriptionSpecificityScorer.ExtractEntry(lines[lineIndices[^1]]);

        var articlePath = string.IsNullOrEmpty(path) ? null : Path.Combine(workspace.WikiRoot, path);
        var articleContent = articlePath is not null && File.Exists(articlePath) ? File.ReadAllText(articlePath) : string.Empty;

        return CatalogDescriptionSpecificityScorer.JudgeAsync(judge, title, description, articleContent, cancellationToken);
    }

    private static string Truncate(string text)
        => text.Length <= 300 ? text : text[..300];
}
