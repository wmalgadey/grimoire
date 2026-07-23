using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Grimoire.EvalRunner;

/// <summary>
/// The 009 observability contract (plan.md ## Observability): capture/replay spans, the
/// `eval_recording_captured` / `eval_replay_result` / `eval_recording_stale` structured
/// log events, and the recordings/replay counters. Log events are emitted inside the
/// active span context and correlate via `task_id`.
/// </summary>
public static class EvalRunnerTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Grimoire.EvalRunner", "1.0.0");

    private static readonly Meter Meter = new("Grimoire.EvalRunner", "1.0.0");

    private static readonly Counter<long> RecordingsCapturedTotal = Meter.CreateCounter<long>(
        "grimoire.eval.recordings_captured_total",
        description: "Captured sample recordings per live capture run, labeled by scenario and provider.");

    private static readonly Counter<long> ReplayResultsTotal = Meter.CreateCounter<long>(
        "grimoire.eval.replay_results_total",
        description: "Replayed sample results, labeled by scenario and trust status.");

    private static readonly EventId RecordingCapturedEvent = new(10, "eval_recording_captured");
    private static readonly EventId ReplayResultEvent = new(11, "eval_replay_result");
    private static readonly EventId RecordingStaleEvent = new(12, "eval_recording_stale");

    public static Activity? StartCaptureRun(string taskId, string scenario, string provider, string? model)
    {
        var span = ActivitySource.StartActivity("eval.capture_run");
        span?.SetTag("task_id", taskId);
        span?.SetTag("scenario", scenario);
        span?.SetTag("provider", provider);
        span?.SetTag("model", model);
        return span;
    }

    public static Activity? StartReplayRun(string taskId, string scenario, string recordingId)
    {
        var span = ActivitySource.StartActivity("eval.replay_run");
        span?.SetTag("task_id", taskId);
        span?.SetTag("scenario", scenario);
        span?.SetTag("recording_id", recordingId);
        return span;
    }

    public static void RecordRecordingCaptured(
        ILogger logger, string taskId, string scenario, int sample, string model, string recordingPath, string provider)
    {
        RecordingsCapturedTotal.Add(
            1,
            new KeyValuePair<string, object?>("scenario", scenario),
            new KeyValuePair<string, object?>("provider", provider));

        logger.LogInformation(
            RecordingCapturedEvent,
            "Eval sample recording captured. task_id={task_id} scenario={scenario} sample={sample} model={model} recording_path={recording_path}",
            taskId,
            scenario,
            sample,
            model,
            recordingPath);
    }

    public static void RecordReplayResult(
        ILogger logger, string taskId, string scenario, int sample, string trustStatus, string? model, string? capturedAt)
    {
        ReplayResultsTotal.Add(
            1,
            new KeyValuePair<string, object?>("scenario", scenario),
            new KeyValuePair<string, object?>("trust_status", trustStatus));

        logger.LogInformation(
            ReplayResultEvent,
            "Eval sample replayed. task_id={task_id} scenario={scenario} sample={sample} trust_status={trust_status} model={model} captured_at={captured_at}",
            taskId,
            scenario,
            sample,
            trustStatus,
            model,
            capturedAt);
    }

    public static void RecordRecordingStale(
        ILogger logger, string scenario, IReadOnlyList<string> changedFingerprints, string recordingPath)
    {
        ReplayResultsTotal.Add(
            1,
            new KeyValuePair<string, object?>("scenario", scenario),
            new KeyValuePair<string, object?>("trust_status", "stale"));

        logger.LogWarning(
            RecordingStaleEvent,
            "Eval recording is stale. scenario={scenario} changed_fingerprints={changed_fingerprints} recording_path={recording_path}",
            scenario,
            string.Join(",", changedFingerprints),
            recordingPath);
    }
}
