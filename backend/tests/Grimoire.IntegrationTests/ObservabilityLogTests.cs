using Grimoire.Hub.OperationalState;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>T036 — Structured log event emission (ADR-005).</summary>
public class ObservabilityLogTests
{
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
}

/// <summary>Test helper: captures ILogger entries for assertion.</summary>
public sealed class CaptureLogger<T> : ILogger<T>
{
    public record LogEntry(LogLevel Level, string EventName, string Message);

    public List<LogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new LogEntry(logLevel, eventId.Name ?? string.Empty, formatter(state, exception)));
    }
}
