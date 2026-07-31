using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Grimoire.AgentRuntime.WikiLog;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T029 (014-wiki-storage-restructure, US3): <see cref="WikiLogAppender"/>'s generalized
/// backstop — generates a conforming <c>## [DATE] TYPE | SUMMARY</c> heading + paragraph
/// for all three agent types (ingest/query/lint), hermetic against a real temp
/// filesystem. Lives in <c>Grimoire.IntegrationTests</c> (not <c>Grimoire.Domain.UnitTests</c>,
/// which references only <c>Grimoire.Domain</c>) since <see cref="WikiLogAppender"/> is a
/// <c>Grimoire.AgentRuntime</c> type this test project already references.
/// </summary>
public class WikiLogAppenderTests
{
    private static readonly ActivitySource TestActivitySource = new("WikiLogAppenderTests");
    private static readonly Meter TestMeter = new("WikiLogAppenderTests");

    [Theory]
    [InlineData("ingest")]
    [InlineData("query")]
    [InlineData("lint")]
    public async Task AppendAsync_GeneratesConformingHeadingAndParagraph_ForEveryAgentType(string type)
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-log-appender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var logPath = Path.Combine(root, "log.md");
            var appender = new WikiLogAppender(TestActivitySource, TestMeter);

            await appender.AppendAsync(
                logPath, type, "completed", "source.md", "Detail text.", "correlation-1", CancellationToken.None);

            var content = await File.ReadAllTextAsync(logPath);
            var lines = content.Split('\n');

            Assert.Matches(@"^## \[\d{4}-\d{2}-\d{2}\] " + type + @" \| .+$", lines[0].TrimEnd('\r'));
            Assert.True(string.IsNullOrWhiteSpace(lines[1]));
            Assert.False(string.IsNullOrWhiteSpace(lines[2]));
            Assert.Contains("correlation-1", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureLogEntryAsync_OnSuccess_SkipsBackstop_WhenCorrelationIdAlreadyPresent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-log-appender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var logPath = Path.Combine(root, "log.md");
            // Simulates the agent's own instructed convention: its paragraph names its
            // own task/turn reference (system-prompt.md "Ingest Log (log.md) Upkeep").
            await File.WriteAllTextAsync(
                logPath,
                "## [2026-07-30] ingest | completed\n\nAgent-authored entry. Task: [[tasks/task-001.md]].\n");

            var appender = new WikiLogAppender(TestActivitySource, TestMeter);
            await appender.EnsureLogEntryAsync(
                logPath, "ingest", "completed", "source.md", "task-001", forceAppend: false, CancellationToken.None);

            var content = await File.ReadAllTextAsync(logPath);
            Assert.DoesNotContain("harness backstop", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureLogEntryAsync_OnSuccess_AppendsBackstop_WhenAgentEntryMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-log-appender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var logPath = Path.Combine(root, "log.md");
            await File.WriteAllTextAsync(logPath, "## [2026-07-29] query | completed\n\nUnrelated earlier entry.\n");

            var appender = new WikiLogAppender(TestActivitySource, TestMeter);
            await appender.EnsureLogEntryAsync(
                logPath, "ingest", "completed", "source.md", "task-002", forceAppend: false, CancellationToken.None);

            var content = await File.ReadAllTextAsync(logPath);
            Assert.Contains("task-002", content, StringComparison.Ordinal);
            Assert.Contains("harness backstop", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureLogEntryAsync_OnFailure_AlwaysAppendsBackstop()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-log-appender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var logPath = Path.Combine(root, "log.md");
            // Even though the correlation id is already present, forceAppend short-circuits
            // the "does an entry already exist" check (mirrors the pre-014 IngestLogAppender
            // failure-path contract: always append on failure).
            await File.WriteAllTextAsync(logPath, "## [2026-07-29] ingest | running\n\nMentions task-003 already.\n");

            var appender = new WikiLogAppender(TestActivitySource, TestMeter);
            await appender.EnsureLogEntryAsync(
                logPath, "ingest", "failed", "source.md", "task-003", forceAppend: true, CancellationToken.None);

            var content = await File.ReadAllTextAsync(logPath);
            Assert.Contains("failed", content, StringComparison.Ordinal);
            Assert.Matches(@"## \[\d{4}-\d{2}-\d{2}\] ingest \| failed", content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// T031/T033 (plan.md ## Observability): <c>wiki_log.backstop_append</c> nests under
    /// whatever is <see cref="Activity.Current"/> on the caller's own
    /// <see cref="ActivitySource"/> at append time — in production that is the calling
    /// agent process's own root/nearest-open span
    /// (<c>{ingest,query,lint}_agent.run</c>, or a nested span still open beneath it, e.g.
    /// Ingest's <c>ingest_agent.finalize_artifact</c>, which itself is a child of
    /// <c>ingest_agent.run</c> — so the trace chain stays connected end-to-end even when
    /// the immediate parent is one level deeper than <c>ingest_agent.run</c> itself; the
    /// same real-vs-literal-plan nesting note <c>LogEntryFormatEnforcementTests</c>
    /// documents for <c>guardrails.format_validate</c>). Also asserts the mandatory
    /// <c>type</c>/<c>task_id_or_run_id</c>/<c>outcome</c> attributes (plan.md's span row).
    /// </summary>
    [Fact]
    public async Task AppendAsync_EmitsBackstopAppendSpan_NestedUnderAmbientActivity_WithMandatoryAttributes()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "WikiLogAppenderTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"wiki-log-appender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var logPath = Path.Combine(root, "log.md");
            var appender = new WikiLogAppender(TestActivitySource, TestMeter);

            Activity? ambient;
            using (ambient = TestActivitySource.StartActivity("test.ambient_run"))
            {
                await appender.AppendAsync(
                    logPath, "ingest", "failed", "source.md", "Detail text.", "task-span-1", CancellationToken.None);
            }

            var thisTrace = activities.Where(a => a.TraceId == ambient!.TraceId).ToList();
            var span = thisTrace.Single(a => a.OperationName == "wiki_log.backstop_append");

            Assert.Equal(ambient!.SpanId.ToHexString(), span.ParentSpanId.ToHexString());
            Assert.Equal("ingest", GetTag(span, "type"));
            Assert.Equal("task-span-1", GetTag(span, "task_id_or_run_id"));
            Assert.Equal("failed", GetTag(span, "outcome"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;
}
