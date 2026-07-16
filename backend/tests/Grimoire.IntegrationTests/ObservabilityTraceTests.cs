using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.Domain.Guardrails;
using Grimoire.Hub.OperationalState;
using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent;
using Grimoire.IngestAgent.Guardrails;
using Grimoire.IngestAgent.IngestLog;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>T037 — Trace span emission via in-process ActivityListener (ADR-005).</summary>
[Collection("IngestAgentObservabilityListeners")]
public class ObservabilityTraceTests
{
    // Old trace tests for deprecated WikiPageWriter/WikiIndexWriter removed as part of T020.
    // Agent loop span coverage is asserted below.

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
            path: "agents/ingest/system-prompt.md",
            sha256: "abc",
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

    [Fact]
    public async Task AgenticIngestTraceSpans_EmitExpectedHierarchyAndAttributes()
    {
        var activities = new ConcurrentQueue<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.IngestAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"trace-contract-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        var pagesDir = Path.Combine(wikiDir, "pages");
        Directory.CreateDirectory(pagesDir);
        await File.WriteAllTextAsync(Path.Combine(pagesDir, "existing.md"), "before");

        var policy = new SafetyPolicy(
            root,
            readPrefixes: [wikiDir + Path.DirectorySeparatorChar],
            writePrefixes: [pagesDir + Path.DirectorySeparatorChar]);
        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(policy, journal, root, taskId: "task-123");
        var fakeModel = new FakeModelClient([
            FakeModelClient.WriteFileTurn("tool-1", "wiki/pages/new.md", "# New page"),
            FakeModelClient.FinalTurn("Trace contract run complete.")]);
        var loop = new AgentLoop(fakeModel, executor);

        using (var runSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.run"))
        {
            runSpan?.SetTag("task_id", "task-123");
            runSpan?.SetTag("model", fakeModel.ModelId);
            runSpan?.SetTag("policy_version", 1);

            using (var loadSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.load_instructions"))
            {
                loadSpan?.SetTag("task_id", "task-123");
                loadSpan?.SetTag("file_count", 2);
            }

            await loop.RunAsync(
                systemPrompt: "You are a test agent.",
                userPrompt: "Integrate the source.",
                taskId: "task-123",
                sourceRef: "source.md",
                sourceContent: "# source\n\ncontent",
                cancellationToken: CancellationToken.None);

            var rollbackJournal = new WriteJournal();
            var rollbackTarget = Path.Combine(root, "wiki", "pages", "rollback.md");
            await File.WriteAllTextAsync(rollbackTarget, "before rollback");
            await rollbackJournal.RecordAsync(rollbackTarget, CancellationToken.None);
            await File.WriteAllTextAsync(rollbackTarget, "after rollback");

            using (var rollbackSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.rollback"))
            {
                rollbackSpan?.SetTag("task_id", "task-123");
                var results = await rollbackJournal.RollbackAsync(CancellationToken.None);
                rollbackSpan?.SetTag("paths_restored", results.Count);
            }

            using (var finalizeSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.finalize_artifact"))
            {
                finalizeSpan?.SetTag("task_id", "task-123");
                finalizeSpan?.SetTag("outcome", "completed");
            }
        }

        // Other test classes run in parallel and emit spans on the same
        // ActivitySource; only this test's trace is under assertion.
        var run = activities.Single(activity => activity.OperationName == "ingest_agent.run");
        var all = activities.Where(activity => activity.TraceId == run.TraceId).ToList();
        var load = all.Single(activity => activity.OperationName == "ingest_agent.load_instructions");
        var turns = all.Where(activity => activity.OperationName == "ingest_agent.model_turn").ToList();
        var tool = all.Single(activity => activity.OperationName == "ingest_agent.tool_call");
        var rollback = all.Single(activity => activity.OperationName == "ingest_agent.rollback");
        var finalize = all.Single(activity => activity.OperationName == "ingest_agent.finalize_artifact");

        // One turn requested the write, one ended the run.
        Assert.Equal(2, turns.Count);
        var turn = turns.Single(activity => GetTag(activity, "stop_reason") == "tool_use");
        var finalTurn = turns.Single(activity => GetTag(activity, "stop_reason") == "end_turn");

        Assert.Equal(run.SpanId.ToHexString(), load.ParentSpanId.ToHexString());
        Assert.All(turns, activity =>
            Assert.Equal(run.SpanId.ToHexString(), activity.ParentSpanId.ToHexString()));
        Assert.Equal(turn.SpanId.ToHexString(), tool.ParentSpanId.ToHexString());
        Assert.Equal(run.SpanId.ToHexString(), rollback.ParentSpanId.ToHexString());
        Assert.Equal(run.SpanId.ToHexString(), finalize.ParentSpanId.ToHexString());

        Assert.Equal("task-123", GetTag(run, "task_id"));
        Assert.Equal("fake-model", GetTag(run, "model"));
        Assert.Equal("1", GetTag(run, "policy_version"));

        Assert.Equal("task-123", GetTag(load, "task_id"));
        Assert.Equal("2", GetTag(load, "file_count"));

        Assert.Equal("task-123", GetTag(turn, "task_id"));
        Assert.Equal("1", GetTag(turn, "turn"));
        Assert.Equal("tool_use", GetTag(turn, "stop_reason"));
        Assert.Equal("100", GetTag(turn, "input_tokens"));
        Assert.Equal("50", GetTag(turn, "output_tokens"));

        Assert.Equal("task-123", GetTag(finalTurn, "task_id"));
        Assert.Equal("2", GetTag(finalTurn, "turn"));
        Assert.Equal("200", GetTag(finalTurn, "input_tokens"));
        Assert.Equal("100", GetTag(finalTurn, "output_tokens"));

        Assert.Equal("task-123", GetTag(tool, "task_id"));
        Assert.Equal("write_file", GetTag(tool, "tool"));
        Assert.Equal(Path.Combine(root, "wiki", "pages", "new.md"), GetTag(tool, "target"));
        Assert.Equal("allowed", GetTag(tool, "decision"));

        Assert.Equal("task-123", GetTag(rollback, "task_id"));
        Assert.Equal("1", GetTag(rollback, "paths_restored"));

        Assert.Equal("task-123", GetTag(finalize, "task_id"));
        Assert.Equal("completed", GetTag(finalize, "outcome"));
    }

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;
}
