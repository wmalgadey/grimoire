using System.Diagnostics;
using System.Collections.Concurrent;
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

    // Old WriteWikiPage span test removed as part of T020 - pipeline replacement
    // New trace tests for agent loop spans (ingest_agent.run, model_turn, tool_call, rollback)
    // will be added as part of T032 phase 6 implementation
}
