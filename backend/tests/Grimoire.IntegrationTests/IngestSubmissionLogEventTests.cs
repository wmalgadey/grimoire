using Grimoire.Hub.IngestSubmission;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T036 (US1) - deterministic validation of event name, level, and mandatory fields for the four
/// US1 structured log events (plan.md ## Observability > Structured Log Events).
/// </summary>
public class IngestSubmissionLogEventTests
{
    [Fact]
    public void LogSubmissionAccepted_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<IngestSubmissionLogEventTests>();
        var submittedAt = DateTimeOffset.UtcNow;

        IngestSubmissionLogEvents.LogSubmissionAccepted(logger, "task-1", "url", submittedAt);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("ingest.submission.accepted", entry.EventName);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, entry.Level);
        Assert.Equal("task-1", entry.Fields["task_id"]);
        Assert.Equal("url", entry.Fields["source_kind"]);
        Assert.Equal(submittedAt, entry.Fields["submitted_at"]);
    }

    [Fact]
    public void LogOriginalPersisted_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<IngestSubmissionLogEventTests>();

        IngestSubmissionLogEvents.LogOriginalPersisted(logger, "task-2", "raw/originals/task-2.html", 1234, ".html");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("ingest.submission.original.persisted", entry.EventName);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, entry.Level);
        Assert.Equal("task-2", entry.Fields["task_id"]);
        Assert.Equal("raw/originals/task-2.html", entry.Fields["original_path"]);
        Assert.Equal(1234L, entry.Fields["size_bytes"]);
        Assert.Equal(".html", entry.Fields["content_type"]);
    }

    [Fact]
    public void LogConversionCompleted_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<IngestSubmissionLogEventTests>();

        IngestSubmissionLogEvents.LogConversionCompleted(logger, "task-3", "pdf_file", "raw/sources/task-3.md", 42);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("ingest.submission.conversion.completed", entry.EventName);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, entry.Level);
        Assert.Equal("task-3", entry.Fields["task_id"]);
        Assert.Equal("pdf_file", entry.Fields["source_kind"]);
        Assert.Equal("raw/sources/task-3.md", entry.Fields["normalized_path"]);
        Assert.Equal(42L, entry.Fields["duration_ms"]);
    }

    [Fact]
    public void LogRunTriggered_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<IngestSubmissionLogEventTests>();

        IngestSubmissionLogEvents.LogRunTriggered(logger, "task-4", 987);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("ingest.run.triggered", entry.EventName);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, entry.Level);
        Assert.Equal("task-4", entry.Fields["task_id"]);
        Assert.Equal(987L, entry.Fields["queued_duration_ms"]);
    }
}
