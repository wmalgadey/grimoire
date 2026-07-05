using Grimoire.Hub.OperationalState;
using Grimoire.IngestAgent;
using Grimoire.IngestAgent.IngestLog;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>T036 — Structured log event emission (ADR-005).</summary>
public class ObservabilityLogTests
{
    [Fact]
    public void IngestAgentStructuredEvents_EmitExpectedNamesLevelsAndFields()
    {
        var logger = new CaptureLogger<ObservabilityLogTests>();

        IngestAgentLogEvents.LogInstructionsLoaded(
            logger,
            taskId: "task-1",
            instructionFiles: "agents/ingest/CLAUDE.md:abc;agents/ingest/skills/wiki-maintenance/SKILL.md:def",
            policyVersion: 1,
            policySha256: "policy-sha");

        IngestAgentLogEvents.LogInstructionsLoadFailed(
            logger,
            taskId: "task-2",
            artifact: "instructions",
            path: "agents/ingest/CLAUDE.md",
            reason: "missing");

        IngestAgentLogEvents.LogToolAllowed(
            logger,
            taskId: "task-3",
            tool: "read_file",
            target: "wiki/index.md",
            turn: 2);

        IngestAgentLogEvents.LogToolDenied(
            logger,
            taskId: "task-4",
            tool: "write_file",
            target: "../secret.txt",
            reason: "traversal",
            turn: 3);

        IngestAgentLogEvents.LogRunRolledBack(
            logger,
            taskId: "task-5",
            pathsRestored: 4,
            restoredOk: true);

        IngestAgentLogEvents.LogBackstopAppended(
            logger,
            taskId: "task-6",
            outcome: "failed");

        IngestAgentLogEvents.LogAgentCompleted(
            logger,
            taskId: "task-7",
            turns: 8,
            pagesCreated: 2,
            pagesUpdated: 1,
            pagesSuperseded: 1,
            denials: 3);

        IngestAgentLogEvents.LogAgentCapExceeded(
            logger,
            taskId: "task-8",
            cap: "turns",
            turns: 50);

        AssertEvent(logger.Entries, "ingest.instructions.loaded", LogLevel.Information, ["task_id", "instruction_files", "policy_version", "policy_sha256"]);
        AssertEvent(logger.Entries, "ingest.instructions.load_failed", LogLevel.Error, ["task_id", "artifact", "path", "reason"]);
        AssertEvent(logger.Entries, "ingest.tool.allowed", LogLevel.Information, ["task_id", "tool", "target", "turn"]);
        AssertEvent(logger.Entries, "ingest.tool.denied", LogLevel.Warning, ["task_id", "tool", "target", "reason", "turn"]);
        AssertEvent(logger.Entries, "ingest.run.rolled_back", LogLevel.Warning, ["task_id", "paths_restored", "restored_ok"]);
        AssertEvent(logger.Entries, "ingest.log.backstop_appended", LogLevel.Warning, ["task_id", "outcome"]);
        AssertEvent(logger.Entries, "ingest.agent.completed", LogLevel.Information, ["task_id", "turns", "pages_created", "pages_updated", "pages_superseded", "denials"]);
        AssertEvent(logger.Entries, "ingest.agent.cap_exceeded", LogLevel.Error, ["task_id", "cap", "turns"]);
    }

    [Fact]
    public async Task IngestLogBackstop_EmitsEvent_WhenEntryIsAppended()
    {
        var root = Path.Combine(Path.GetTempPath(), $"log-backstop-{Guid.NewGuid():N}");
        var logPath = Path.Combine(root, "log.md");
        Directory.CreateDirectory(root);

        var logger = new CaptureLogger<IngestLogAppender>();
        var appender = new IngestLogAppender(logger);

        await appender.EnsureLogEntryAsync(
            logPath,
            outcome: "failed",
            sourceRef: "source.md",
            taskId: "task-backstop",
            forceAppend: true,
            CancellationToken.None);

        AssertEvent(logger.Entries, "ingest.log.backstop_appended", LogLevel.Warning, ["task_id", "outcome"]);
    }

    [Fact]
    public async Task RestartReconciler_Emits_ReconciliationLogEvent_WithMandatoryFields()
    {
        var root = Path.Combine(Path.GetTempPath(), $"log-obs-{Guid.NewGuid():N}");
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

        var logger = new CaptureLogger<RestartReconciler>();
        var reconciler = new RestartReconciler(repository, logger);
        await reconciler.ReconcileRunningTasksAsync(tasksDir, logPath);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("ingest.task.reconciled", entry.EventName);
        Assert.Contains(taskId, entry.Message);
        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    [Fact]
    public async Task RestartReconciler_LogEvent_ContainsInterruptionReason()
    {
        var root = Path.Combine(Path.GetTempPath(), $"log-obs-{Guid.NewGuid():N}");
        var tasksDir = Path.Combine(root, "tasks");
        var logPath = Path.Combine(root, "log.md");
        Directory.CreateDirectory(tasksDir);
        await File.WriteAllTextAsync(logPath, string.Empty);

        var taskId = $"task-{Guid.NewGuid():N}";
        var taskPath = Path.Combine(tasksDir, $"{taskId}.md");
        await File.WriteAllTextAsync(taskPath,
            "---\n" +
            $"task_id: {taskId}\n" +
            "type: ingest\nstatus: running\nagent: ingest\n" +
            "started_at: 2026-07-04T00:00:00Z\ncompleted_at: null\n" +
            "source_ref: \"source.md\"\npages_touched: []\nfailure_reason: null\n" +
            "---\n\nRunning\n");

        var dbPath = Path.Combine(root, "state.db");
        var repository = new OperationalStateRepository(dbPath);
        await repository.InitializeAsync();
        await repository.UpsertAsync(new OperationalTaskState(taskId, "running", null, DateTimeOffset.UtcNow));

        var logger = new CaptureLogger<RestartReconciler>();
        var reconciler = new RestartReconciler(repository, logger);
        await reconciler.ReconcileRunningTasksAsync(tasksDir, logPath);

        Assert.Single(logger.Entries);
        Assert.Contains("Hub restarted", logger.Entries[0].Message);
    }

    private static void AssertEvent(
        List<CaptureLoggerEntry> entries,
        string eventName,
        LogLevel level,
        string[] requiredFields)
    {
        var entry = Assert.Single(entries.Where(e => e.EventName == eventName));
        Assert.Equal(level, entry.Level);

        foreach (var field in requiredFields)
        {
            Assert.True(entry.Fields.ContainsKey(field), $"Missing field '{field}' for event '{eventName}'.");
        }
    }
}

/// <summary>Test helper: captures ILogger entries for assertion.</summary>
public sealed class CaptureLogger<T> : ILogger<T>
{
    public List<CaptureLoggerEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IEnumerable<KeyValuePair<string, object?>> structured)
        {
            foreach (var pair in structured)
            {
                if (pair.Key == "{OriginalFormat}")
                {
                    continue;
                }

                fields[pair.Key] = pair.Value;
            }
        }

        Entries.Add(new CaptureLoggerEntry(logLevel, eventId.Name ?? string.Empty, formatter(state, exception), fields));
    }
}

public sealed record CaptureLoggerEntry(
    LogLevel Level,
    string EventName,
    string Message,
    IReadOnlyDictionary<string, object?> Fields);
