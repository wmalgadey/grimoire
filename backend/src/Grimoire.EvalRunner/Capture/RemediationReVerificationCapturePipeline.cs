using Grimoire.AgentRuntime.Core.Adapters.Replay;
using Grimoire.EvalRunner.Providers;
using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Scoring;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging;

namespace Grimoire.EvalRunner.Capture;

/// <summary>One remediation-execution sample's capture-run outcome.</summary>
public sealed record RemediationReVerificationCaptureSampleResult(
    int Sample,
    string RunId,
    bool Captured,
    bool? Pass,
    string? Detail);

/// <summary>One remediation-execution scenario's capture-run outcome.</summary>
public sealed record RemediationReVerificationCaptureScenarioResult(
    string ScenarioId,
    string? Model,
    double Threshold,
    double SuccessRate,
    bool ThresholdMet,
    bool Stored,
    IReadOnlyList<RemediationReVerificationCaptureSampleResult> Samples,
    string? Detail);

/// <summary>
/// The remediation-execution re-verification capture tier (T039, 015-lint-board-parity,
/// FR-018) — mirrors <see cref="LintCapturePipeline"/> for the sibling invocation mode:
/// per sample, a fresh sandbox copy of the scenario's fixture wiki
/// (<see cref="QueryEvalSandbox"/>, agent-agnostic), one spawned real remediation-
/// execution agent process, live deterministic scoring against the terminal event's
/// reported outcome, then a wholesale atomic replacement of the scenario's recording set.
/// Partial scenarios never reach the store.
/// </summary>
public sealed class RemediationReVerificationCapturePipeline
{
    private static readonly TimeSpan CaptureSampleBudget = TimeSpan.FromMinutes(20);

    private readonly RecordingStore _store;
    private readonly EvalPaths _paths;
    private readonly LintAgentProcessInvoker _invoker;
    private readonly ILogger _logger;

    public RemediationReVerificationCapturePipeline(
        RecordingStore store, EvalPaths paths, LintAgentProcessInvoker invoker, ILogger logger)
    {
        _store = store;
        _paths = paths;
        _invoker = invoker;
        _logger = logger;
    }

    public async Task<RemediationReVerificationCaptureScenarioResult> RunScenarioAsync(
        RemediationReVerificationScenarioDefinition scenario,
        ProviderConfiguration provider,
        int requestedSampleCount,
        CancellationToken cancellationToken)
    {
        var providerLabel = EvalObservability.ProviderLabel(provider.Kind);
        var fixtureWikiRoot = _paths.FixtureWikiRoot(scenario.FixtureName);
        var recordings = new List<RecordedSample>();
        var sampleResults = new List<RemediationReVerificationCaptureSampleResult>();
        string? model = null;

        for (var i = 0; i < requestedSampleCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sampleNumber = i + 1;
            var runId = $"capture-{scenario.Id}-{sampleNumber:00}-{Guid.NewGuid():N}";

            using var sandbox = QueryEvalSandbox.Create(fixtureWikiRoot, $"remediation-reverify-capture-{scenario.Id}-{sampleNumber:00}");
            var wikiRoot = sandbox.WikiRoot;

            var captureScratch = Path.Combine(Path.GetTempPath(), "grimoire-eval-runner", "capture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(captureScratch);
            var capturePath = Path.Combine(captureScratch, $"sample-{sampleNumber:00}.json");

            try
            {
                using var span = EvalRunnerTelemetry.StartCaptureRun(runId, scenario.Id, providerLabel, provider.Model);

                var run = await _invoker.RunRemediationExecutionAsync(
                    runId, runId, wikiRoot, _paths, AgentModelMode.Capture(capturePath, provider), CaptureSampleBudget,
                    sandbox.WriteLocksDir, scenario.ProposalTitle, scenario.ProposalDescription, scenario.ProposalTargetPath,
                    cancellationToken);

                if (run.TimedOut)
                {
                    EvalObservability.RecordSampleTimeout(
                        _logger, $"{scenario.Id}-{sampleNumber}", providerLabel, provider.Model, CaptureSampleBudget.TotalSeconds);
                    sampleResults.Add(new RemediationReVerificationCaptureSampleResult(
                        sampleNumber, runId, Captured: false, Pass: null,
                        Detail: $"Sample {sampleNumber} exceeded the {CaptureSampleBudget.TotalMinutes:0}min capture budget."));
                    continue;
                }

                if (!File.Exists(capturePath))
                {
                    sampleResults.Add(new RemediationReVerificationCaptureSampleResult(
                        sampleNumber, runId, Captured: false, Pass: null,
                        Detail: $"Sample {sampleNumber} produced no captured run " +
                            $"(exit {run.ExitCode}, completed={run.Completed}): {Truncate(run.StdErr)}"));
                    continue;
                }

                var rawCapture = RecordingSerialization.Load(capturePath);
                model ??= rawCapture.Model;

                var score = RemediationReVerificationScorer.Score(
                    scenario.ExpectedOutcome, new RemediationReVerificationSampleRunData(run.RemediationOutcome, run.Reason));

                recordings.Add(rawCapture with
                {
                    Sample = sampleNumber,
                    TaskId = runId,
                    Outcome = new RecordedOutcome("completed", score.Checks),
                });

                sampleResults.Add(new RemediationReVerificationCaptureSampleResult(sampleNumber, runId, Captured: true, score.Pass, Detail: null));

                EvalRunnerTelemetry.RecordRecordingCaptured(
                    _logger, runId, scenario.Id, sampleNumber, rawCapture.Model,
                    _store.SamplePath(scenario.Id, $"sample-{sampleNumber:00}.json"), providerLabel);
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

        var allCaptured = sampleResults.Count == requestedSampleCount && sampleResults.All(r => r.Captured);
        var successes = sampleResults.Count(r => r.Pass == true);
        var rate = sampleResults.Count == 0 ? 0 : (double)successes / sampleResults.Count;

        if (!allCaptured)
        {
            return new RemediationReVerificationCaptureScenarioResult(
                scenario.Id, model, scenario.Threshold, rate, rate >= scenario.Threshold, Stored: false, sampleResults,
                Detail: "Not every sample produced a recording — the scenario's recording set was NOT replaced (no partial stores).");
        }

        var fingerprints = RemediationReVerificationStalenessCheck.CurrentFingerprints(scenario, _paths);
        _store.ReplaceScenario(scenario.Id, capturedAt: DateTimeOffset.UtcNow, model: model ?? "unknown", providerLabel, fingerprints, recordings);

        return new RemediationReVerificationCaptureScenarioResult(
            scenario.Id, model, scenario.Threshold, rate, rate >= scenario.Threshold, Stored: true, sampleResults, Detail: null);
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300];
}
