using System.Diagnostics;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry.Metrics;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T016 + T018 (023-task-ui-improvements, plan.md ## Observability): the logging, metric and
/// trace contracts for reactivation. The signals are obtained through the production
/// composition root — <c>AddHubTelemetry</c> with in-memory exporters attached to the same
/// provider builders the Hub uses — never a test-only always-on listener, per Principle IV's
/// rule written from the Feature-003 sampler incident.
/// </summary>
[Collection("HubActivityListenerObservability")]
public class IngestReactivationObservabilityTests
{
    private static readonly TimeSpan LivenessWindow = TimeSpan.FromSeconds(60);
    private static readonly DateTimeOffset Epoch = new(2026, 8, 13, 7, 0, 0, TimeSpan.Zero);

    // ── T016: structured log events ────────────────────────────────────────────────

    [Fact]
    public async Task ReactivationLifecycle_EmitsAllThreeLogEvents_WithDeclaredLevelsAndMandatoryFields()
    {
        var time = new FakeTimeProvider(Epoch);
        var launcher = new FakeAgentProcessLauncher(autoPlay: false) { GoSilentIngestLaunches = int.MaxValue };
        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: launcher, livenessWindow: LivenessWindow, timeProvider: time);

        await fixture.Coordinator.EnqueueAsync("task-signals", Path.Combine(fixture.Root, "src.md"), null);
        await IngestRunReactivationTests.WaitForLaunchesAsync(launcher, 1);
        await IngestRunReactivationTests.DriveToExhaustionAsync(fixture, launcher, time, "task-signals");

        var entries = fixture.CoordinatorLogger.Entries;

        // ingest.run.liveness_interrupted — WARN: task_id, attempt, next_delay_seconds
        var interrupted = entries.Where(e => e.EventName == "ingest.run.liveness_interrupted").ToList();
        Assert.Equal(3, interrupted.Count);
        Assert.All(interrupted, entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Equal("task-signals", entry.Fields["task_id"]);
            Assert.True(entry.Fields.ContainsKey("attempt"));
            Assert.True(entry.Fields.ContainsKey("next_delay_seconds"));
        });
        Assert.Equal([1, 2, 3], interrupted.Select(e => e.Fields["attempt"]));
        Assert.Equal([10L, 30L, 90L], interrupted.Select(e => e.Fields["next_delay_seconds"]));

        // ingest.run.reactivated — INFO: task_id, attempt
        var reactivated = entries.Where(e => e.EventName == "ingest.run.reactivated").ToList();
        Assert.Equal(3, reactivated.Count);
        Assert.All(reactivated, entry =>
        {
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Equal("task-signals", entry.Fields["task_id"]);
            Assert.True(entry.Fields.ContainsKey("attempt"));
        });
        Assert.Equal([1, 2, 3], reactivated.Select(e => e.Fields["attempt"]));

        // ingest.run.reactivation_exhausted — ERROR: task_id, attempts
        var exhausted = Assert.Single(entries, e => e.EventName == "ingest.run.reactivation_exhausted");
        Assert.Equal(LogLevel.Error, exhausted.Level);
        Assert.Equal("task-signals", exhausted.Fields["task_id"]);
        Assert.Equal(3, exhausted.Fields["attempts"]);
    }

    [Fact]
    public async Task LivenessInterruption_DoesNotEmitTheFinalFailureEvent_UntilAttemptsAreExhausted()
    {
        var time = new FakeTimeProvider(Epoch);
        var launcher = new FakeAgentProcessLauncher(autoPlay: false) { GoSilentIngestLaunches = int.MaxValue };
        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: launcher, livenessWindow: LivenessWindow, timeProvider: time);

        await fixture.Coordinator.EnqueueAsync("task-not-yet-failed", Path.Combine(fixture.Root, "src.md"), null);
        await IngestRunReactivationTests.WaitForLaunchesAsync(launcher, 1);

        time.Advance(LivenessWindow + TimeSpan.FromSeconds(1));
        await PollAsync.WaitAsync(
            () => fixture.CoordinatorLogger.Entries.Any(e => e.EventName == "ingest.run.liveness_interrupted"),
            TimeSpan.FromSeconds(15),
            "Expected an 'ingest.run.liveness_interrupted' entry.");

        // The pre-existing event keeps its meaning: final failure only (plan.md ## Observability).
        Assert.DoesNotContain(fixture.CoordinatorLogger.Entries, e => e.EventName == "ingest.run.liveness_failed");
        Assert.DoesNotContain(fixture.CoordinatorLogger.Entries, e => e.EventName == "ingest.run.reactivation_exhausted");
    }

    // ── T018: metric + trace contracts, through production wiring ──────────────────

    [Fact]
    public async Task ReactivationsTotal_IsExported_ThroughProductionMeterRegistration_WithDeclaredLabelSetOnly()
    {
        var exportedMetrics = new List<Metric>();
        var time = new FakeTimeProvider(Epoch);
        var launcher = new FakeAgentProcessLauncher(autoPlay: false) { GoSilentIngestLaunches = int.MaxValue };
        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: launcher, livenessWindow: LivenessWindow, timeProvider: time);
        using var host = await IngestApiHost.BuildAsync(fixture, exportedActivities: null, exportedMetrics: exportedMetrics);

        await fixture.Coordinator.EnqueueAsync("task-metric", Path.Combine(fixture.Root, "src.md"), null);
        await IngestRunReactivationTests.WaitForLaunchesAsync(launcher, 1);
        await IngestRunReactivationTests.DriveToExhaustionAsync(fixture, launcher, time, "task-metric");

        host.Services.GetRequiredService<MeterProvider>().ForceFlush();

        var metric = Assert.Single(exportedMetrics, m => m.Name == "wiki.ingest.reactivations_total");
        var outcomes = new List<string>();
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            foreach (var tag in point.Tags)
            {
                Assert.Equal("outcome", tag.Key);
                outcomes.Add((string)tag.Value!);
            }
        }

        // Declared label set only: {attempted, exhausted} — nothing else is ever passed.
        Assert.All(outcomes, outcome => Assert.Contains(outcome, new[] { "attempted", "exhausted" }));
        Assert.Contains("attempted", outcomes);
        Assert.Contains("exhausted", outcomes);
    }

    [Fact]
    public async Task ReactivationSpan_IsExportedAsARoot_WithDeclaredAttributes()
    {
        var exported = new IngestApiHost.SynchronizedActivityCollection();
        var time = new FakeTimeProvider(Epoch);
        var launcher = new FakeAgentProcessLauncher(autoPlay: false) { GoSilentIngestLaunches = int.MaxValue };
        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: launcher, livenessWindow: LivenessWindow, timeProvider: time);
        using var host = await IngestApiHost.BuildAsync(fixture, exported);

        await fixture.Coordinator.EnqueueAsync("task-span", Path.Combine(fixture.Root, "src.md"), null);
        await IngestRunReactivationTests.WaitForLaunchesAsync(launcher, 1);

        time.Advance(LivenessWindow + TimeSpan.FromSeconds(1));
        await IngestRunReactivationTests.WaitForHistoryAsync(
            fixture, "task-span", h => h.Any(e => e.Status == "liveness_interrupted"));
        time.Advance(TimeSpan.FromSeconds(10));

        var span = await exported.WaitForSpanAsync("ingest_hub.reactivation");

        // Root: a reactivation runs off a backoff timer, not inside a request or the
        // original supervision scope — parentage must not be inherited by accident.
        Assert.Equal(default, span.ParentSpanId);
        Assert.Null(span.Parent);

        Assert.Equal("task-span", span.GetTagItem("task_id"));
        Assert.Equal(1, span.GetTagItem("attempt"));
        Assert.Equal(10L, span.GetTagItem("delay_seconds"));
    }
}
