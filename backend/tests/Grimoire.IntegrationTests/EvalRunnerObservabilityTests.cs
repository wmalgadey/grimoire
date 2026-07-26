using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Grimoire.EvalRunner;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T038/T039 — deterministic validation of the 009 eval-runner observability contract
/// (plan.md ## Observability): the `eval_recording_captured` / `eval_replay_result` /
/// `eval_recording_stale` structured log events (name, level, every mandatory field),
/// the `eval.capture_run` / `eval.replay_run` root spans with their required attributes,
/// and the recordings/replay counters — all correlated via `task_id`. Runs in the
/// standard PR pipeline; no provider, no process spawns.
/// </summary>
public class EvalRunnerObservabilityTests
{
    [Fact]
    public void RecordRecordingCaptured_EmitsEventWithNameLevelAndMandatoryFields()
    {
        var logger = new CaptureLogger<EvalRunnerObservabilityTests>();

        EvalRunnerTelemetry.RecordRecordingCaptured(
            logger, "capture-task-1", "convention-adherence", 3, "nvidia-model", "/tmp/rec/sample-03.json", "affordable");

        var entry = Assert.Single(logger.Entries, e => e.EventName == "eval_recording_captured");
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("capture-task-1", entry.Fields["task_id"]?.ToString());
        Assert.Equal("convention-adherence", entry.Fields["scenario"]?.ToString());
        Assert.Equal("3", entry.Fields["sample"]?.ToString());
        Assert.Equal("nvidia-model", entry.Fields["model"]?.ToString());
        Assert.Equal("/tmp/rec/sample-03.json", entry.Fields["recording_path"]?.ToString());
    }

    [Fact]
    public void RecordReplayResult_EmitsEventWithNameLevelAndMandatoryFields()
    {
        var logger = new CaptureLogger<EvalRunnerObservabilityTests>();

        EvalRunnerTelemetry.RecordReplayResult(
            logger, "replay-task-1", "adversarial-source", 2, "trusted", "nvidia-model", "2026-07-23T00:00:00Z");

        var entry = Assert.Single(logger.Entries, e => e.EventName == "eval_replay_result");
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("replay-task-1", entry.Fields["task_id"]?.ToString());
        Assert.Equal("adversarial-source", entry.Fields["scenario"]?.ToString());
        Assert.Equal("2", entry.Fields["sample"]?.ToString());
        Assert.Equal("trusted", entry.Fields["trust_status"]?.ToString());
        Assert.Equal("nvidia-model", entry.Fields["model"]?.ToString());
        Assert.Equal("2026-07-23T00:00:00Z", entry.Fields["captured_at"]?.ToString());
    }

    [Fact]
    public void RecordRecordingStale_EmitsWarningWithNameAndMandatoryFields()
    {
        var logger = new CaptureLogger<EvalRunnerObservabilityTests>();

        EvalRunnerTelemetry.RecordRecordingStale(
            logger, "steering-adoption", ["system_prompt", "judge_prompt"], "data/evals/recordings/steering-adoption");

        var entry = Assert.Single(logger.Entries, e => e.EventName == "eval_recording_stale");
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("steering-adoption", entry.Fields["scenario"]?.ToString());
        Assert.Equal("system_prompt,judge_prompt", entry.Fields["changed_fingerprints"]?.ToString());
        Assert.Equal("data/evals/recordings/steering-adoption", entry.Fields["recording_path"]?.ToString());
    }

    [Fact]
    public void CaptureAndReplaySpans_AreRoots_WithRequiredCorrelationAttributes()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.EvalRunner",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        using (var captureSpan = EvalRunnerTelemetry.StartCaptureRun("span-task-1", "convention-adherence", "affordable", "nvidia-model"))
        {
            Assert.NotNull(captureSpan);
            Assert.Null(captureSpan!.Parent);
        }

        using (var replaySpan = EvalRunnerTelemetry.StartReplayRun("span-task-2", "adversarial-source", "sample-01.json"))
        {
            Assert.NotNull(replaySpan);
            Assert.Null(replaySpan!.Parent);
            replaySpan.SetTag("trust_status", "trusted");
        }

        var capture = Assert.Single(activities, a => a.OperationName == "eval.capture_run");
        Assert.Equal("span-task-1", GetTag(capture, "task_id"));
        Assert.Equal("convention-adherence", GetTag(capture, "scenario"));
        Assert.Equal("affordable", GetTag(capture, "provider"));
        Assert.Equal("nvidia-model", GetTag(capture, "model"));

        var replay = Assert.Single(activities, a => a.OperationName == "eval.replay_run");
        Assert.Equal("span-task-2", GetTag(replay, "task_id"));
        Assert.Equal("adversarial-source", GetTag(replay, "scenario"));
        Assert.Equal("sample-01.json", GetTag(replay, "recording_id"));
        Assert.Equal("trusted", GetTag(replay, "trust_status"));
    }

    [Fact]
    public void Counters_IncrementWithDeclaredLabels()
    {
        var measurements = new ConcurrentBag<(string Instrument, string Scenario, string Label, long Value)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.EvalRunner")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            string? scenario = null;
            string? label = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "scenario")
                {
                    scenario = tag.Value?.ToString();
                }
                else if (tag.Key is "provider" or "trust_status")
                {
                    label = tag.Value?.ToString();
                }
            }

            measurements.Add((instrument.Name, scenario ?? "", label ?? "", measurement));
        });
        listener.Start();

        var logger = new CaptureLogger<EvalRunnerObservabilityTests>();
        EvalRunnerTelemetry.RecordRecordingCaptured(logger, "t1", "convention-adherence", 1, "m", "/tmp/r.json", "affordable");
        EvalRunnerTelemetry.RecordReplayResult(logger, "t2", "convention-adherence", 1, "trusted", "m", "now");
        EvalRunnerTelemetry.RecordRecordingStale(logger, "convention-adherence", ["system_prompt"], "/tmp/rec");

        Assert.Contains(measurements, m =>
            m.Instrument == "grimoire.eval.recordings_captured_total" && m.Scenario == "convention-adherence" && m.Label == "affordable" && m.Value == 1);
        Assert.Contains(measurements, m =>
            m.Instrument == "grimoire.eval.replay_results_total" && m.Scenario == "convention-adherence" && m.Label == "trusted" && m.Value == 1);
        Assert.Contains(measurements, m =>
            m.Instrument == "grimoire.eval.replay_results_total" && m.Scenario == "convention-adherence" && m.Label == "stale" && m.Value == 1);
    }

    private static string? GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString();
}
