using Grimoire.Hub.Realtime;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T031 (US3) — IngestTaskRecordWatcher publishes a debounced `taskRecordChanged` event per
/// atomic-rename write (contracts/task-record-changed-event.md), coalesces rapid
/// successive writes, ignores writer temp files, and self-restarts after a simulated IO
/// failure. Uses a real Kestrel host + a real SignalR client, like
/// IngestLifecycleRealtimeTests, and a real temp `TasksDir` (Principle II — no port for a
/// local-filesystem observer).
/// </summary>
public class IngestTaskRecordWatcherTests
{
    [Fact]
    public async Task AtomicRenameWrite_PublishesExactlyOneEvent_WithinFreshnessBudget()
    {
        await using var harness = await Harness.StartAsync();

        var taskId = "ingest-watcher-1";
        harness.WriteRecordAtomically(taskId, "running");

        var events = await harness.WaitForEventsAsync(taskId, expectedCount: 1, timeout: TimeSpan.FromSeconds(5));

        var evt = Assert.Single(events);
        Assert.Equal(taskId, evt.TaskId);
        Assert.False(string.IsNullOrWhiteSpace(evt.EventId));
        Assert.True(evt.ChangedAt <= DateTimeOffset.UtcNow);
    }

    // 019-fast-test-tier (ADR-021 R4): the writes must land within the watcher's 300ms
    // debounce window for coalescing to be exercised at all, and the subsequent settle
    // wait IS the test's own subject (does the window actually coalesce them into one
    // event?) — there is no earlier observable signal to poll for, since the assertion
    // is the absence of further events. Exempt from the fixed-wait ban (FR-005).
    [Trait("TimingDependent", "true")]
    [Fact]
    public async Task RapidSuccessiveWrites_WithinDebounceWindow_CoalesceToOneEvent()
    {
        await using var harness = await Harness.StartAsync();

        var taskId = "ingest-watcher-2";
        for (var i = 0; i < 5; i++)
        {
            harness.WriteRecordAtomically(taskId, "running", narrative: $"update {i}");
            await Task.Delay(20);
        }

        // Give the 300ms debounce window plus margin to settle, then assert no more arrive.
        await Task.Delay(TimeSpan.FromSeconds(1));
        var events = harness.EventsFor(taskId);

        Assert.Single(events);
    }

    // 019-fast-test-tier (ADR-021 R4): asserts the absence of events for temp files — there
    // is no positive completion signal to poll for, so a fixed wait past the debounce
    // window is the only way to gain confidence nothing arrives. Exempt (FR-005).
    [Trait("TimingDependent", "true")]
    [Fact]
    public async Task TempFiles_NeverProduceEvents()
    {
        await using var harness = await Harness.StartAsync();

        var tempPath = Path.Combine(harness.TasksDir, ".ingest-watcher-3.abc123.tmp");
        await File.WriteAllTextAsync(tempPath, "not a real record");

        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Empty(harness.EventsFor("ingest-watcher-3"));
    }

    // 019-fast-test-tier (ADR-021 R4): the watcher's documented 1s self-restart delay is
    // itself the behavior under test — waiting past it is the point, not a proxy for an
    // earlier completion signal. Exempt from the fixed-wait ban (FR-005).
    [Trait("TimingDependent", "true")]
    [Fact]
    public async Task WatcherIoFailure_TriggersSelfRestart_AndEventsResume()
    {
        await using var harness = await Harness.StartAsync();

        harness.Watcher.SimulateWatcherErrorForTests(new IOException("simulated watch handle loss"));

        // Self-restart has a 1s delay before re-arming; wait past it, then prove the fresh
        // watcher delivers again via the same handshake used at startup before asserting
        // on the real record.
        await Task.Delay(TimeSpan.FromSeconds(2));
        await harness.ArmWatcherAsync();

        var taskId = "ingest-watcher-4";
        harness.WriteRecordAtomically(taskId, "running");

        var events = await harness.WaitForEventsAsync(taskId, expectedCount: 1, timeout: TimeSpan.FromSeconds(5));
        Assert.Single(events);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HubConnection _connection;
        private readonly List<IngestTaskRecordChangedEvent> _received = [];
        private readonly object _lock = new();

        public string TasksDir { get; }
        public IngestTaskRecordWatcher Watcher { get; }

        private Harness(WebApplication app, HubConnection connection, string tasksDir, IngestTaskRecordWatcher watcher)
        {
            _app = app;
            _connection = connection;
            TasksDir = tasksDir;
            Watcher = watcher;
        }

        public static async Task<Harness> StartAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"grimoire-task-record-watcher-{Guid.NewGuid():N}");
            // TasksDir is a base-level sibling of the content root (014-wiki-storage-restructure),
            // not nested under `wiki/`.
            var tasksDir = Path.Combine(root, "tasks");
            Directory.CreateDirectory(tasksDir);

            var resolvedPaths = TestResolvedGrimoirePathsFactory.Create(root) with { TasksDir = tasksDir };

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSignalR();
            var app = builder.Build();
            app.MapHub<IngestLifecycleHub>("/hubs/ingest-lifecycle");
            await app.StartAsync();

            var baseUrl = app.Urls.First();
            var connection = new HubConnectionBuilder()
                .WithUrl($"{baseUrl}/hubs/ingest-lifecycle")
                .Build();

            var harness = new Harness(app, connection, tasksDir,
                new IngestTaskRecordWatcher(resolvedPaths, new IngestLifecyclePublisher(app.Services.GetRequiredService<IHubContext<IngestLifecycleHub>>())));

            connection.On<IngestTaskRecordChangedEvent>("taskRecordChanged", e =>
            {
                lock (harness._lock) { harness._received.Add(e); }
            });

            await connection.StartAsync();
            await harness.Watcher.StartAsync(CancellationToken.None);
            await harness.ArmWatcherAsync();

            return harness;
        }

        /// <summary>
        /// Deterministic arming handshake: macOS FSEvents streams can drop events raised
        /// before the stream is fully started, so a fixed settle delay flakes. Write
        /// sentinel records until one round-trips as a `taskRecordChanged` event; only
        /// then is the watcher provably delivering.
        /// </summary>
        public async Task ArmWatcherAsync()
        {
            const string sentinelId = "watcher-arming-sentinel";
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var seen = EventsFor(sentinelId).Count;
                WriteRecordAtomically(sentinelId, "running", narrative: $"arming attempt {attempt}");

                // Each probe needs the 300 ms debounce window plus delivery margin.
                var delivered = await PollAsync.TryWaitAsync(
                    () => EventsFor(sentinelId).Count > seen,
                    TimeSpan.FromSeconds(1));

                if (delivered)
                {
                    return;
                }
            }

            throw new TimeoutException("FileSystemWatcher never delivered the arming sentinel event.");
        }

        public void WriteRecordAtomically(string taskId, string status, string? narrative = null)
        {
            var path = Path.Combine(TasksDir, $"{taskId}.md");
            var tempPath = Path.Combine(TasksDir, $".{taskId}.{Guid.NewGuid():N}.tmp");
            var content =
                $"""
                ---
                task_id: {taskId}
                status: {status}
                agent: ingest
                started_at: {DateTimeOffset.UtcNow:O}
                completed_at: null
                source_ref: null
                original_ref: null
                failure_reason: null
                ---

                {narrative ?? "watcher test record"}
                """;
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, path, overwrite: true);
        }

        public List<IngestTaskRecordChangedEvent> EventsFor(string taskId)
        {
            lock (_lock) { return [.. _received.Where(e => e.TaskId == taskId)]; }
        }

        public async Task<List<IngestTaskRecordChangedEvent>> WaitForEventsAsync(string taskId, int expectedCount, TimeSpan timeout)
        {
            await PollAsync.WaitAsync(
                () => EventsFor(taskId).Count >= expectedCount,
                timeout,
                $"Expected {expectedCount} taskRecordChanged event(s) for '{taskId}', got {EventsFor(taskId).Count}.");

            return EventsFor(taskId);
        }

        public async ValueTask DisposeAsync()
        {
            await Watcher.StopAsync(CancellationToken.None);
            Watcher.Dispose();
            await _connection.StopAsync();
            await _connection.DisposeAsync();
            await _app.StopAsync();
            await _app.DisposeAsync();
            // TasksDir is now a direct child of `root` (base-level sibling), so a single
            // parent hop reaches `root` — was a double hop when TasksDir sat under `wiki/`.
            try { Directory.Delete(Path.GetDirectoryName(TasksDir)!, recursive: true); } catch { /* best-effort */ }
        }
    }
}
