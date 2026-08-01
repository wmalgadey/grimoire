using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using Grimoire.Hub;
using Grimoire.Hub.RemediationTasks;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T038 (015-lint-board-parity, US4) — deterministic observability tests for the US4-scoped
/// rows of plan.md ## Observability that T027's <see cref="LintRemediationObservabilityTests"/>
/// does not cover (that class owns only the US3 proposal-materialization signals; per the
/// ADR-013/N1 naming rule this class does not reference <c>Grimoire.Hub.LintDispatch</c>
/// directly, so it keeps the plain name): log events <c>hub.remediation.task_authorized</c>/
/// <c>task_dismissed</c>/<c>authorization_withdrawn</c>/<c>execution_started</c>/
/// <c>execution_completed</c> (name/level/mandatory fields); spans
/// <c>hub.remediation.authorize</c>/<c>execution_dispatch</c>/<c>run_supervision</c>/
/// <c>re_verify</c> (names, parentage, <c>task_id</c> correlation); metrics
/// <c>tasks_authorized_total</c>/<c>tasks_dismissed_total</c>/<c>tasks_withdrawn_total</c>/
/// <c>tasks_executed_total{outcome}</c>/<c>queue_depth</c>. In-process listener/CaptureLogger
/// pattern per ADR-005, mirroring <see cref="LintRemediationObservabilityTests"/>/
/// <see cref="QueryConversationLogEventTests"/>.
/// </summary>
public class RemediationObservabilityTests
{
    // ── log events ──────────────────────────────────────────────────────────────

    [Fact]
    public void TaskAuthorizedEvent_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<RemediationLifecyclePublisher>();

        RemediationLifecycleLogEvents.LogTaskAuthorized(logger, taskId: "2026-08-01-remediation-obs1");

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "hub.remediation.task_authorized"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.True(entry.Fields.ContainsKey("task_id"), "Missing mandatory field 'task_id'.");
        Assert.Equal("2026-08-01-remediation-obs1", entry.Fields["task_id"]?.ToString());
    }

    [Fact]
    public void TaskDismissedEvent_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<RemediationLifecyclePublisher>();

        RemediationLifecycleLogEvents.LogTaskDismissed(logger, taskId: "2026-08-01-remediation-obs2");

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "hub.remediation.task_dismissed"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.True(entry.Fields.ContainsKey("task_id"), "Missing mandatory field 'task_id'.");
        Assert.Equal("2026-08-01-remediation-obs2", entry.Fields["task_id"]?.ToString());
    }

    [Fact]
    public void AuthorizationWithdrawnEvent_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<RemediationLifecyclePublisher>();

        RemediationLifecycleLogEvents.LogAuthorizationWithdrawn(logger, taskId: "2026-08-01-remediation-obs3");

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "hub.remediation.authorization_withdrawn"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.True(entry.Fields.ContainsKey("task_id"), "Missing mandatory field 'task_id'.");
        Assert.Equal("2026-08-01-remediation-obs3", entry.Fields["task_id"]?.ToString());
    }

    [Fact]
    public void ExecutionStartedEvent_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<RemediationRunCoordinator>();

        RemediationLifecycleLogEvents.LogExecutionStarted(logger, taskId: "2026-08-01-remediation-obs4");

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "hub.remediation.execution_started"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.True(entry.Fields.ContainsKey("task_id"), "Missing mandatory field 'task_id'.");
        Assert.Equal("2026-08-01-remediation-obs4", entry.Fields["task_id"]?.ToString());
    }

    [Fact]
    public void ExecutionCompletedEvent_WithReason_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<RemediationRunCoordinator>();

        RemediationLifecycleLogEvents.LogExecutionCompleted(
            logger, taskId: "2026-08-01-remediation-obs5", outcome: "not_applicable",
            reason: "The page gained a tags list after this action was proposed.");

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "hub.remediation.execution_completed"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.True(entry.Fields.ContainsKey("task_id"), "Missing mandatory field 'task_id'.");
        Assert.True(entry.Fields.ContainsKey("outcome"), "Missing mandatory field 'outcome'.");
        Assert.True(entry.Fields.ContainsKey("reason"), "Missing mandatory field 'reason'.");
        Assert.Equal("2026-08-01-remediation-obs5", entry.Fields["task_id"]?.ToString());
        Assert.Equal("not_applicable", entry.Fields["outcome"]?.ToString());
        Assert.Equal(
            "The page gained a tags list after this action was proposed.", entry.Fields["reason"]?.ToString());
    }

    [Fact]
    public void ExecutionCompletedEvent_WithoutReason_StillCarriesTheReasonField_AsNull()
    {
        // plan.md ## Observability: "reason (reason nullable except on failed/not_applicable)"
        // — the field itself is always present, just null for a plain `completed` outcome.
        var logger = new CaptureLogger<RemediationRunCoordinator>();

        RemediationLifecycleLogEvents.LogExecutionCompleted(
            logger, taskId: "2026-08-01-remediation-obs6", outcome: "completed", reason: null);

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "hub.remediation.execution_completed"));
        Assert.True(entry.Fields.ContainsKey("reason"), "Missing mandatory field 'reason'.");
        Assert.Null(entry.Fields["reason"]);
        Assert.Equal("completed", entry.Fields["outcome"]?.ToString());
    }

    // ── metrics ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordRemediationTaskAuthorized_Increments_TasksAuthorizedTotal()
    {
        var total = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.remediation.tasks_authorized_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => { lock (this) { total += value; } });
        listener.Start();

        HubMetrics.RecordRemediationTaskAuthorized();

        lock (this)
        {
            Assert.Equal(1L, total);
        }
    }

    [Fact]
    public void RecordRemediationTaskDismissed_Increments_TasksDismissedTotal()
    {
        var total = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.remediation.tasks_dismissed_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => { lock (this) { total += value; } });
        listener.Start();

        HubMetrics.RecordRemediationTaskDismissed();

        lock (this)
        {
            Assert.Equal(1L, total);
        }
    }

    [Fact]
    public void RecordRemediationTaskWithdrawn_Increments_TasksWithdrawnTotal()
    {
        var total = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.remediation.tasks_withdrawn_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => { lock (this) { total += value; } });
        listener.Start();

        HubMetrics.RecordRemediationTaskWithdrawn();

        lock (this)
        {
            Assert.Equal(1L, total);
        }
    }

    [Fact]
    public void RecordRemediationTaskExecuted_Increments_TasksExecutedTotal_WithOutcomeTag()
    {
        var measurements = new List<(long Value, string Outcome)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.remediation.tasks_executed_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var outcome = tags.ToArray().FirstOrDefault(t => t.Key == "outcome").Value?.ToString() ?? "";
            lock (measurements) { measurements.Add((value, outcome)); }
        });
        listener.Start();

        HubMetrics.RecordRemediationTaskExecuted("not_applicable");

        lock (measurements)
        {
            Assert.Contains(measurements, m => m.Value == 1L && m.Outcome == "not_applicable");
        }
    }

    [Fact]
    public void RecordRemediationQueueDepth_RecordsTheGauge()
    {
        var values = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.remediation.queue_depth")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => { lock (values) { values.Add(value); } });
        listener.Start();

        HubMetrics.RecordRemediationQueueDepth(3);

        lock (values)
        {
            Assert.Contains(3L, values);
        }
    }

    // ── spans ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthorizeSpan_IsRoot_WithTaskId_OverRealHttp()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        using var harness = await RemediationEndpointHostHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-authspan";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var response = await harness.Client.PostAsync($"/api/remediation-tasks/{taskId}/authorize", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var authorize = Assert.Single(activities.Where(
            a => a.OperationName == "hub.remediation.authorize" && GetTag(a, "task_id") == taskId));
        Assert.True(string.IsNullOrEmpty(authorize.ParentId), "hub.remediation.authorize must be a root span.");
    }

    [Fact]
    public async Task ExecutionDispatchAndRunSupervisionSpans_ParentChildLinkage_WithTaskId()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        await using var app = await StartHubHostAsync();
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, launcher);

        const string taskId = "2026-08-01-remediation-spanchain";
        await harness.InsertAuthorizedAsync(taskId, DateTimeOffset.UtcNow);

        await harness.Coordinator.TryStartNextAsync();
        var handle = Assert.Single(harness.Launcher.Handles);
        handle.EmitEvent("completed", taskId, new { summary = "Applied the fix." });

        await harness.WaitForStateAsync(taskId, RemediationTaskStates.Completed);

        var dispatch = Assert.Single(activities.Where(
            a => a.OperationName == "hub.remediation.execution_dispatch" && GetTag(a, "task_id") == taskId));
        Assert.True(string.IsNullOrEmpty(dispatch.ParentId), "hub.remediation.execution_dispatch must be a root span.");

        var supervision = Assert.Single(activities.Where(
            a => a.OperationName == "hub.remediation.run_supervision" && GetTag(a, "task_id") == taskId));
        Assert.Equal(dispatch.SpanId.ToHexString(), supervision.ParentSpanId.ToHexString());
        Assert.Equal(dispatch.TraceId, supervision.TraceId);
    }

    [Fact]
    public async Task ReVerifySpan_IsChildOfRunSupervision_WithTaskIdAndStillApplicableTrue_WhenApplied()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        await using var app = await StartHubHostAsync();
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, launcher);

        const string taskId = "2026-08-01-remediation-reverify-applied";
        await harness.InsertAuthorizedAsync(taskId, DateTimeOffset.UtcNow);

        await harness.Coordinator.TryStartNextAsync();
        var handle = Assert.Single(harness.Launcher.Handles);
        // No `remediationOutcome` field at all — contract: absent ⇒ applied/completed.
        handle.EmitEvent("completed", taskId, new { summary = "Applied the fix." });

        await harness.WaitForStateAsync(taskId, RemediationTaskStates.Completed);

        var supervision = Assert.Single(activities.Where(
            a => a.OperationName == "hub.remediation.run_supervision" && GetTag(a, "task_id") == taskId));
        var reverify = Assert.Single(activities.Where(
            a => a.OperationName == "hub.remediation.re_verify" && GetTag(a, "task_id") == taskId));

        Assert.Equal(supervision.SpanId.ToHexString(), reverify.ParentSpanId.ToHexString());
        Assert.Equal(supervision.TraceId, reverify.TraceId);
        Assert.Equal("True", GetTag(reverify, "still_applicable"));
    }

    [Fact]
    public async Task ReVerifySpan_StillApplicableFalse_WhenRemediationOutcomeIsNotApplicable()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        await using var app = await StartHubHostAsync();
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, launcher);

        const string taskId = "2026-08-01-remediation-reverify-notapplicable";
        await harness.InsertAuthorizedAsync(taskId, DateTimeOffset.UtcNow);

        await harness.Coordinator.TryStartNextAsync();
        var handle = Assert.Single(harness.Launcher.Handles);
        handle.EmitEventWithFields("completed", taskId, new Dictionary<string, object?>
        {
            ["remediationOutcome"] = "not_applicable",
            ["reason"] = "Tags already present; proposal is moot.",
        });

        await harness.WaitForStateAsync(taskId, RemediationTaskStates.NotApplicable);

        var reverify = Assert.Single(activities.Where(
            a => a.OperationName == "hub.remediation.re_verify" && GetTag(a, "task_id") == taskId));
        Assert.Equal("False", GetTag(reverify, "still_applicable"));
    }

    // ── shared setup (mirrors RemediationRunCoordinatorTests) ──────────────────────

    private static async Task<WebApplication> StartHubHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        var app = builder.Build();
        app.MapHub<RemediationLifecycleHub>("/hubs/remediation-lifecycle");
        await app.StartAsync();
        return app;
    }

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;
}
