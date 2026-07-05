using System.Diagnostics;
using System.Collections.Concurrent;
using Grimoire.Hub.OperationalState;
using Grimoire.IngestAgent;
using Grimoire.IngestAgent.Guardrails;
using Grimoire.IngestAgent.IngestLog;

namespace Grimoire.IntegrationTests;

/// <summary>T037 — Trace span emission via in-process ActivityListener (ADR-005).</summary>
public class ObservabilityTraceTests
{
    // Old trace tests for deprecated WikiPageWriter/WikiIndexWriter removed as part of T020
    // New trace tests for agent loop spans will be added in phase 6 (T032)

    [Fact]
    public async Task IngestLogAppender_Creates_AppendLog_Span()
    {
        var spanNames = new ConcurrentQueue<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.IngestAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => spanNames.Enqueue(a.OperationName)
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"trace-test-{Guid.NewGuid():N}");
        var logPath = Path.Combine(root, "log.md");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(logPath, string.Empty);

        var appender = new IngestLogAppender();
        await appender.AppendAsync(logPath, "completed", "source.md", "Create pages/test.md", "task-001", CancellationToken.None);

        Assert.Contains("ingest_agent.append_log", spanNames);
    }

    [Fact]
    public async Task IngestLogAppender_EnsureLogEntry_CreatesMissingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trace-test-{Guid.NewGuid():N}");
        var logPath = Path.Combine(root, "wiki", "log.md");

        var appender = new IngestLogAppender();
        await appender.EnsureLogEntryAsync(logPath, "completed", "source.md", "task-001", forceAppend: false, CancellationToken.None);

        Assert.True(File.Exists(logPath));
        var content = await File.ReadAllTextAsync(logPath);
        Assert.Contains("task-001", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObservabilitySignals_CreateChildSpans_ForLogsMetricsAndHubEvents()
    {
        var spanNames = new ConcurrentQueue<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name is "Grimoire.IngestAgent" or "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => spanNames.Enqueue(activity.OperationName)
        };
        ActivitySource.AddActivityListener(listener);

        var ingestLogger = new CaptureLogger<ObservabilityTraceTests>();
        IngestAgentLogEvents.LogInstructionsLoaded(
            ingestLogger,
            taskId: "task-obs-1",
            instructionFiles: "agents/ingest/CLAUDE.md:abc",
            policyVersion: 1,
            policySha256: "policy-sha");

        IngestAgentMetrics.RecordToolCall("read_file", "allowed");

        var root = Path.Combine(Path.GetTempPath(), $"trace-test-{Guid.NewGuid():N}");
        var tasksDir = Path.Combine(root, "tasks");
        var logPath = Path.Combine(root, "log.md");
        Directory.CreateDirectory(tasksDir);
        await File.WriteAllTextAsync(logPath, string.Empty);

        var taskId = $"task-{Guid.NewGuid():N}";
        var taskPath = Path.Combine(tasksDir, $"{taskId}.md");
        await File.WriteAllTextAsync(taskPath,
            "---\n" +
            $"task_id: {taskId}\n" +
            "type: ingest\n" +
            "status: running\n" +
            "agent: ingest\n" +
            "started_at: 2026-07-04T00:00:00Z\n" +
            "completed_at: null\n" +
            "source_ref: \"source.md\"\n" +
            "pages_touched: []\n" +
            "failure_reason: null\n" +
            "---\n\nRunning\n");

        var dbPath = Path.Combine(root, "state.db");
        var repository = new OperationalStateRepository(dbPath);
        await repository.InitializeAsync();
        await repository.UpsertAsync(new OperationalTaskState(taskId, "running", null, DateTimeOffset.UtcNow));

        var reconciler = new RestartReconciler(repository);
        await reconciler.ReconcileRunningTasksAsync(tasksDir, logPath);

        Assert.Contains("ingest.instructions.loaded", spanNames);
        Assert.Contains("wiki.ingest.tool_calls_total", spanNames);
        Assert.Contains("ingest.task.reconciled", spanNames);
        Assert.Contains("wiki.ingest.tasks_reconciled_total", spanNames);
    }

    // Old WriteWikiPage span test removed as part of T020 - pipeline replacement
    // New trace tests for agent loop spans (ingest_agent.run, model_turn, tool_call, rollback)
    // will be added as part of T032 phase 6 implementation
}
