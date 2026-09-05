using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Grimoire.Hub;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.RemediationTasks;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T027 (015-lint-board-parity, US3) — deterministic observability tests for the
/// proposal-materialization signals of plan.md ## Observability (in-process
/// listener/CaptureLogger pattern per ADR-005, mirroring <see cref="LintLogEventTests"/>/
/// <see cref="LintMetricsTests"/>/<see cref="LintTraceTests"/>): log event
/// <c>hub.lint.remediation_task_proposed</c> (name/level/mandatory fields), span
/// <c>hub.lint.propose_remediation_tasks</c> (parented under
/// <c>hub.lint.run_supervision</c>, <c>run_id</c>/<c>proposed_count</c> attributes),
/// metric <c>wiki.lint.remediation_tasks_proposed_total</c>, and T023's
/// <c>hub.remediation_lifecycle_updates_total{stage}</c> broadcast counter. US4/US5
/// extend this class with their own signal rows (T038/T044).
/// </summary>
[Collection("HubActivityListenerObservability")]
public class LintRemediationObservabilityTests
{
    [Fact]
    public void RemediationTaskProposedEvent_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<LintRunCoordinator>();

        LintLifecycleLogEvents.LogRemediationTaskProposed(
            logger, runId: "2026-08-01-lint-obs", taskId: "2026-08-01-remediation-obs");

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "hub.lint.remediation_task_proposed"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.True(entry.Fields.ContainsKey("run_id"), "Missing mandatory field 'run_id'.");
        Assert.True(entry.Fields.ContainsKey("task_id"), "Missing mandatory field 'task_id'.");
        Assert.Equal("2026-08-01-lint-obs", entry.Fields["run_id"]?.ToString());
        Assert.Equal("2026-08-01-remediation-obs", entry.Fields["task_id"]?.ToString());
    }

    [Fact]
    public async Task ProposeRemediationTasksSpan_IsChildOfRunSupervision_WithRunIdAndProposedCount()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"grimoire-remediation-obs-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.FindingsDir);
        try
        {
            var repository = new OperationalStateRepository(paths.StateDbPath);
            await repository.InitializeAsync();

            var launcher = new FakeAgentProcessLauncher(autoPlay: true)
            {
                ScriptedLintTerminalMetadata = new Dictionary<string, object?>
                {
                    ["proposedActions"] = new object[]
                    {
                        new { title = "First proposal", description = "First description." },
                        new { title = "Second proposal", description = "Second description." },
                    },
                },
            };

            var coordinator = new LintRunCoordinator(
                launcher,
                new LintFindingsReportStore(paths, NullLogger<LintFindingsReportStore>.Instance),
                paths,
                logger: NullLogger<LintRunCoordinator>.Instance,
                stateRepository: repository,
                remediationRecordStore: new RemediationTaskRecordStore(paths));

            var result = await coordinator.TriggerAsync();
            var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
            var runId = accepted.Run.RunId;

            await PollAsync.WaitAsync(
                () => coordinator.GetRun(runId) is { IsTerminal: true } run && run.FindingsReportPath is not null,
                TimeSpan.FromSeconds(10),
                $"Expected lint run '{runId}' to reach a terminal status with a Findings Report within 10s.");

            var supervision = Assert.Single(activities.Where(
                a => a.OperationName == "hub.lint.run_supervision" && GetTag(a, "run_id") == runId));
            var propose = Assert.Single(activities.Where(
                a => a.OperationName == "hub.lint.propose_remediation_tasks" && GetTag(a, "run_id") == runId));

            Assert.Equal(supervision.SpanId.ToHexString(), propose.ParentSpanId.ToHexString());
            Assert.Equal(supervision.TraceId, propose.TraceId);
            Assert.Equal("2", GetTag(propose, "proposed_count"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void RecordRemediationTaskProposed_Increments_ProposedTotal_WithRunIdTag()
    {
        var measurements = new List<(long Value, string RunId)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.lint.remediation_tasks_proposed_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var runId = tags.ToArray().FirstOrDefault(t => t.Key == "run_id").Value?.ToString() ?? "";
            lock (measurements) { measurements.Add((value, runId)); }
        });
        listener.Start();

        HubMetrics.RecordRemediationTaskProposed("2026-08-01-lint-metric");

        lock (measurements)
        {
            Assert.Contains(measurements, m => m.Value == 1L && m.RunId == "2026-08-01-lint-metric");
        }
    }

    [Fact]
    public void RecordRemediationLifecycleUpdate_Increments_LifecycleUpdatesTotal_WithStageTag()
    {
        var measurements = new List<(long Value, string Stage)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "hub.remediation_lifecycle_updates_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var stage = tags.ToArray().FirstOrDefault(t => t.Key == "stage").Value?.ToString() ?? "";
            lock (measurements) { measurements.Add((value, stage)); }
        });
        listener.Start();

        HubMetrics.RecordRemediationLifecycleUpdate("proposed");

        lock (measurements)
        {
            Assert.Contains(measurements, m => m.Value == 1L && m.Stage == "proposed");
        }
    }

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;
}
