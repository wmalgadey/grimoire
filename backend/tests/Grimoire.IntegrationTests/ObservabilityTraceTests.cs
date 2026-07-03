using System.Diagnostics;
using Grimoire.IngestAgent.IngestLog;
using Grimoire.IngestAgent.WikiIndex;
using Grimoire.IngestAgent.WikiWrite;

namespace Grimoire.IntegrationTests;

/// <summary>T037 — Trace span emission via in-process ActivityListener (ADR-005).</summary>
public class ObservabilityTraceTests
{
    [Fact]
    public async Task WikiPageWriter_Creates_WriteWikiPage_Span()
    {
        var spanNames = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.IngestAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => spanNames.Add(a.OperationName)
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"trace-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var writer = new WikiPageWriter();
        await writer.WriteAsync(root, "test-page.md", "# Test\nContent", CancellationToken.None);

        Assert.Contains("ingest_agent.write_wiki_page", spanNames);
    }

    [Fact]
    public async Task WikiIndexWriter_Creates_UpdateIndex_Span()
    {
        var spanNames = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.IngestAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => spanNames.Add(a.OperationName)
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"trace-test-{Guid.NewGuid():N}");
        var indexPath = Path.Combine(root, "index.md");
        Directory.CreateDirectory(root);

        var writer = new WikiIndexWriter();
        await writer.UpdateAsync(indexPath, "General", "My Page", "pages/my-page.md", "Summary.", CancellationToken.None);

        Assert.Contains("ingest_agent.update_index", spanNames);
    }

    [Fact]
    public async Task IngestLogAppender_Creates_AppendLog_Span()
    {
        var spanNames = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.IngestAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => spanNames.Add(a.OperationName)
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
    public async Task WriteWikiPage_Span_Carries_PagePath_Tag()
    {
        Activity? capturedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.IngestAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.OperationName == "ingest_agent.write_wiki_page")
                    capturedActivity = a;
            }
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"trace-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var writer = new WikiPageWriter();
        var fullPath = await writer.WriteAsync(root, "my-page.md", "# Page", CancellationToken.None);

        Assert.NotNull(capturedActivity);
        Assert.Equal(fullPath, capturedActivity.GetTagItem("page_path")?.ToString());
    }
}
