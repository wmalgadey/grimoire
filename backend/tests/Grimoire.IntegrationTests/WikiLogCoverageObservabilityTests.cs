using System.Diagnostics;
using System.Diagnostics.Metrics;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Telemetry;
using Grimoire.AgentRuntime.WikiLog;
using Grimoire.Domain.Guardrails;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Grimoire.IntegrationTests;

/// <summary>
/// 025-agent-owned-log T029 — the SC-009/FR-012a observability contract for the signal
/// that replaced the deleted backstop.
///
/// Collected through the <b>production composition root</b>: the provider is built by the
/// real <see cref="AgentTelemetryBootstrap"/>, with the real resource, the real registered
/// source/meter names, and the real default sampler — the test only attaches an in-memory
/// exporter to that same construction. Constitution Principle IV requires this: standing
/// up a test-only always-on <c>ActivityListener</c> proves the emitting line ran, not that
/// the signal reaches an observer in production, and Feature 003 shipped green trace tests
/// over a Hub that exported nothing at all.
///
/// The suite asserts the whole contract in one place — event name, level and mandatory
/// fields; metric increment and its label; both span names, their parent/child linkage and
/// shared correlation attribute — plus the property that makes this signal legitimate at
/// all: it never writes to the wiki.
/// </summary>
public class WikiLogCoverageObservabilityTests
{
    private const string SourceName = "Grimoire.IngestAgent.CoverageContractTests";
    private const string MeterName = "Grimoire.IngestAgent.CoverageContractTests";

    private const string SeededLog =
        "## [2026-08-01] ingest | created retrieval-patterns\n\n" +
        "Created [[concepts/retrieval-patterns]] from source \"notes.md\". Task: task-earlier.\n";

    [Fact]
    public async Task RunThatChangedTheWikiWithoutLogging_EmitsTheFullSignal_AndWritesNothingToTheWiki()
    {
        var exportedActivities = new List<Activity>();
        var exportedMetrics = new List<Metric>();
        var capturedLogs = new CaptureLogger<WikiLogCoverageObserver>();

        using var telemetry = AgentTelemetryBootstrap.Build(
            serviceName: "Grimoire.IngestAgent",
            activitySourceName: SourceName,
            meterName: MeterName,
            configureTracing: tracing => tracing.AddInMemoryExporter(exportedActivities),
            configureMetrics: metrics => metrics.AddInMemoryExporter(exportedMetrics));

        using var activitySource = new ActivitySource(SourceName);
        using var meter = new Meter(MeterName);

        var (root, wikiDir, logPath) = CreateWorkspace();
        try
        {
            await File.WriteAllTextAsync(logPath, SeededLog);
            var seededBytes = await File.ReadAllBytesAsync(logPath);

            var executor = NewExecutor(root, wikiDir, logPath);

            // A page write, and deliberately no log write — the case the backstop covered.
            var pageResult = await executor.ExecuteAsync(
                "write_file",
                """{"path": "wiki/tech/new-page.md", "content": "# New\n\nBody."}""",
                turn: 1,
                CancellationToken.None);
            Assert.False(pageResult.IsError);

            var observer = new WikiLogCoverageObserver(activitySource, meter, capturedLogs);

            Activity? ambient;
            using (ambient = activitySource.StartActivity("ingest_agent.finalize_artifact"))
            {
                var outcome = observer.Observe(executor, "ingest", "task-001");
                Assert.Equal(WikiLogCoverageOutcome.NotLogged, outcome);
            }

            // The signal never touches the wiki (SC-009).
            Assert.Equal(seededBytes, await File.ReadAllBytesAsync(logPath));

            // --- Structured log event ---------------------------------------------------
            var entry = Assert.Single(
                capturedLogs.Entries,
                e => e.EventName == "wiki.log.change_not_logged");
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Equal("ingest", entry.Fields["type"]?.ToString());
            Assert.Equal("task-001", entry.Fields["task_id_or_run_id"]?.ToString());
            Assert.Equal("1", entry.Fields["wiki_content_writes"]?.ToString());

            // --- Spans ------------------------------------------------------------------
            telemetry.ForceFlushTraces();
            var thisTrace = exportedActivities.Where(a => a.TraceId == ambient!.TraceId).ToList();

            var checkSpan = Assert.Single(thisTrace, a => a.OperationName == "wiki_log.coverage_check");
            Assert.Equal(ambient!.SpanId.ToHexString(), checkSpan.ParentSpanId.ToHexString());
            Assert.Equal("ingest", GetTag(checkSpan, "type"));
            Assert.Equal("task-001", GetTag(checkSpan, "task_id_or_run_id"));
            Assert.Equal("1", GetTag(checkSpan, "wiki_content_writes"));
            Assert.Equal("not_logged", GetTag(checkSpan, "outcome"));

            var eventSpan = Assert.Single(thisTrace, a => a.OperationName == "wiki.log.change_not_logged");
            // The log-event span nests inside the coverage-check span.
            Assert.Equal(checkSpan.SpanId.ToHexString(), eventSpan.ParentSpanId.ToHexString());
            Assert.Equal("log", GetTag(eventSpan, "signal_type"));
            Assert.Equal("wiki.log.change_not_logged", GetTag(eventSpan, "event_name"));
            Assert.Equal("Warning", GetTag(eventSpan, "level"));
            // Shared correlation attribute joins the signal to the run's trace.
            Assert.Equal("task-001", GetTag(eventSpan, "task_id_or_run_id"));
            Assert.Equal("ingest", GetTag(eventSpan, "type"));
            Assert.Equal("1", GetTag(eventSpan, "wiki_content_writes"));

            // --- Metric -----------------------------------------------------------------
            telemetry.ForceFlushMetrics();
            var metric = Assert.Single(exportedMetrics, m => m.Name == "wiki.log.unlogged_change_total");
            var (sum, typeLabel) = ReadLongSum(metric);
            Assert.Equal(1, sum);
            Assert.Equal("ingest", typeLabel);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The negative control, without which the assertions above would pass for a signal
    /// that fires on every run: a run that wrote both a page and the log reports
    /// <c>outcome=logged</c> and emits no <c>wiki.log.change_not_logged</c>.
    /// </summary>
    [Fact]
    public async Task RunThatLoggedItsChange_EmitsNoWarning_AndReportsOutcomeLogged()
    {
        var exportedActivities = new List<Activity>();
        var capturedLogs = new CaptureLogger<WikiLogCoverageObserver>();

        using var telemetry = AgentTelemetryBootstrap.Build(
            serviceName: "Grimoire.IngestAgent",
            activitySourceName: SourceName + ".Covered",
            meterName: MeterName + ".Covered",
            configureTracing: tracing => tracing.AddInMemoryExporter(exportedActivities));

        using var activitySource = new ActivitySource(SourceName + ".Covered");
        using var meter = new Meter(MeterName + ".Covered");

        var (root, wikiDir, logPath) = CreateWorkspace();
        try
        {
            await File.WriteAllTextAsync(logPath, SeededLog);
            var executor = NewExecutor(root, wikiDir, logPath);

            await executor.ExecuteAsync(
                "write_file",
                """{"path": "wiki/tech/new-page.md", "content": "# New\n\nBody."}""",
                turn: 1,
                CancellationToken.None);

            await executor.ExecuteAsync("read_file", """{"path": "wiki/log.md"}""", turn: 2, CancellationToken.None);
            var newEntry = "## [2026-08-17] ingest | created new-page\n\nCreated [[tech/new-page]]. Task: task-002.\n";
            var logResult = await executor.ExecuteAsync(
                "write_file",
                $$"""{"path": "wiki/log.md", "content": {{System.Text.Json.JsonSerializer.Serialize(newEntry + SeededLog)}}}""",
                turn: 3,
                CancellationToken.None);
            Assert.False(logResult.IsError);

            var observer = new WikiLogCoverageObserver(activitySource, meter, capturedLogs);

            Activity? ambient;
            using (ambient = activitySource.StartActivity("ingest_agent.finalize_artifact"))
            {
                var outcome = observer.Observe(executor, "ingest", "task-002");
                Assert.Equal(WikiLogCoverageOutcome.Logged, outcome);
            }

            Assert.DoesNotContain(capturedLogs.Entries, e => e.EventName == "wiki.log.change_not_logged");

            telemetry.ForceFlushTraces();
            var thisTrace = exportedActivities.Where(a => a.TraceId == ambient!.TraceId).ToList();
            var checkSpan = Assert.Single(thisTrace, a => a.OperationName == "wiki_log.coverage_check");
            Assert.Equal("logged", GetTag(checkSpan, "outcome"));
            Assert.DoesNotContain(thisTrace, a => a.OperationName == "wiki.log.change_not_logged");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A run that changed nothing is silent too — <c>no_change</c>, no warning. This is the
    /// row that keeps the signal from firing on every routine lookup turn.
    /// </summary>
    [Fact]
    public async Task RunThatChangedNothing_EmitsNoWarning_AndReportsOutcomeNoChange()
    {
        var capturedLogs = new CaptureLogger<WikiLogCoverageObserver>();
        using var activitySource = new ActivitySource(SourceName + ".NoChange");
        using var meter = new Meter(MeterName + ".NoChange");

        var (root, wikiDir, logPath) = CreateWorkspace();
        try
        {
            await File.WriteAllTextAsync(logPath, SeededLog);
            var executor = NewExecutor(root, wikiDir, logPath);

            await executor.ExecuteAsync("read_file", """{"path": "wiki/log.md"}""", turn: 1, CancellationToken.None);

            var observer = new WikiLogCoverageObserver(activitySource, meter, capturedLogs);
            var outcome = observer.Observe(executor, "query", "turn-003");

            Assert.Equal(WikiLogCoverageOutcome.NoChange, outcome);
            Assert.DoesNotContain(capturedLogs.Entries, e => e.EventName == "wiki.log.change_not_logged");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (long Sum, string? TypeLabel) ReadLongSum(Metric metric)
    {
        long total = 0;
        string? type = null;
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            total += point.GetSumLong();
            foreach (var tag in point.Tags)
            {
                if (tag.Key == "type")
                {
                    type = tag.Value?.ToString();
                }
            }
        }

        return (total, type);
    }

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;

    private static (string Root, string WikiDir, string LogPath) CreateWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-log-coverage-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        Directory.CreateDirectory(Path.Combine(wikiDir, "tech"));
        return (root, wikiDir, Path.Combine(wikiDir, "log.md"));
    }

    private static GuardedToolExecutor NewExecutor(string root, string wikiDir, string logPath)
    {
        var policy = new SafetyPolicy(
            root,
            readPrefixes: [wikiDir + Path.DirectorySeparatorChar],
            writePrefixes:
            [
                Path.Combine(wikiDir, "tech") + Path.DirectorySeparatorChar,
                Path.Combine(wikiDir, "index.md"),
                logPath,
            ]);

        return new GuardedToolExecutor(
            policy,
            new WriteJournal(),
            root,
            taskId: "task-coverage",
            writeLocksDir: Path.Combine(root, "write-locks"),
            logPath: logPath);
    }
}
