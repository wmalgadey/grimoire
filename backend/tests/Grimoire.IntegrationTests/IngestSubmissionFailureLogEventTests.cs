using Grimoire.Hub.IngestSubmission;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T060 (US3) - deterministic validation of event name, level, and mandatory fields for the two
/// US3 failure log events (plan.md ## Observability > Structured Log Events).
/// </summary>
public class IngestSubmissionFailureLogEventTests
{
    [Fact]
    public void LogUrlFetchFailed_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<IngestSubmissionFailureLogEventTests>();

        IngestSubmissionLogEvents.LogUrlFetchFailed(logger, "task-1", "https://example.test/x", "URL fetch timeout after 30s", 504);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("ingest.submission.url_fetch.failed", entry.EventName);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, entry.Level);
        Assert.Equal("task-1", entry.Fields["task_id"]);
        Assert.Equal("https://example.test/x", entry.Fields["url"]);
        Assert.Equal("URL fetch timeout after 30s", entry.Fields["failure_reason"]);
        Assert.Equal(504, entry.Fields["http_status"]);
    }

    [Fact]
    public void LogConversionFailed_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<IngestSubmissionFailureLogEventTests>();

        IngestSubmissionLogEvents.LogConversionFailed(logger, "task-2", "office_file", "PdfReadError: bad structure");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("ingest.submission.conversion.failed", entry.EventName);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, entry.Level);
        Assert.Equal("task-2", entry.Fields["task_id"]);
        Assert.Equal("office_file", entry.Fields["source_kind"]);
        Assert.Equal("PdfReadError: bad structure", entry.Fields["failure_reason"]);
    }
}
