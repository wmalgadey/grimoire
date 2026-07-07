using Grimoire.Hub.Realtime;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T047 (US2) - deterministic validation of event name, level, and mandatory fields for
/// `ingest.lifecycle.published` (plan.md ## Observability > Structured Log Events).
/// </summary>
public class IngestLifecycleLogEventTests
{
    [Fact]
    public async Task PublishAsync_EmitsLifecyclePublishedLogEvent()
    {
        var logger = new CaptureLogger<IngestLifecyclePublisher>();
        var publisher = new IngestLifecyclePublisher(new NullHubContext(), logger);

        await publisher.PublishAsync("task-1", "converting", "queued");

        var entry = Assert.Single(logger.Entries, e => e.EventName == "ingest.lifecycle.published");
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("task-1", entry.Fields["task_id"]);
        Assert.Equal("converting", entry.Fields["from_stage"]);
        Assert.Equal("queued", entry.Fields["to_stage"]);
    }
}
