using Grimoire.Hub.IngestSubmission;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T039 (Phase 6) - deterministic validation of event name, level, and mandatory fields
/// for every 006 structured log event row (plan.md ## Observability > Structured Log
/// Events): task_record.served for each trigger outcome, task_record.change_published on
/// a debounced publish, and task_record.watch_failed on a watcher IO failure.
/// </summary>
public class TaskRecordLogEventTests
{
    [Theory]
    [InlineData("ok")]
    [InlineData("missing")]
    [InlineData("unparseable")]
    public void LogTaskRecordServed_EmitsExpectedNameLevelAndFields_ForEveryOutcome(string outcome)
    {
        var logger = new CaptureLogger<TaskRecordLogEventTests>();

        IngestSubmissionLogEvents.LogTaskRecordServed(logger, "task-1", outcome, contentLength: 42);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("task_record.served", entry.EventName);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("task-1", entry.Fields["task_id"]);
        Assert.Equal(outcome, entry.Fields["outcome"]);
        Assert.Equal(42, entry.Fields["content_length"]);
    }

    [Fact]
    public void LogTaskRecordChangePublished_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<TaskRecordLogEventTests>();
        var changedAt = DateTimeOffset.UtcNow;

        IngestSubmissionLogEvents.LogTaskRecordChangePublished(logger, "task-2", "evt-1", changedAt);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("task_record.change_published", entry.EventName);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("task-2", entry.Fields["task_id"]);
        Assert.Equal("evt-1", entry.Fields["event_id"]);
        Assert.Equal(changedAt, entry.Fields["changed_at"]);
    }

    [Fact]
    public void LogTaskRecordWatchFailed_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<TaskRecordLogEventTests>();

        IngestSubmissionLogEvents.LogTaskRecordWatchFailed(logger, "/data/wiki/tasks", "simulated watch handle loss");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("task_record.watch_failed", entry.EventName);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("/data/wiki/tasks", entry.Fields["path"]);
        Assert.Equal("simulated watch handle loss", entry.Fields["reason"]);
    }
}
