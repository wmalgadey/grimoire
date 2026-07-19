using Grimoire.Hub.Realtime;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T031 (US3) — TaskRecordWatcher publishes a debounced `taskRecordChanged` event per
/// atomic-rename write (contracts/task-record-changed-event.md), coalesces rapid
/// successive writes, ignores writer temp files, and self-restarts after a simulated IO
/// failure. Uses a real Kestrel host + a real SignalR client, like
/// IngestLifecycleRealtimeTests, and a real temp `TasksDir` (Principle II — no port for a
/// local-filesystem observer).
/// </summary>
public class TaskRecordWatcherTests
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

    [Fact]
    public async Task TempFiles_NeverProduceEvents()
    {
        await using var harness = await Harness.StartAsync();

        var tempPath = Path.Combine(harness.TasksDir, ".ingest-watcher-3.abc123.tmp");
        await File.WriteAllTextAsync(tempPath, "not a real record");

        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Empty(harness.EventsFor("ingest-watcher-3"));
    }

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
        private readonly List<TaskRecordChangedEvent> _received = [];
        private readonly object _lock = new();

        public string TasksDir { get; }
        public TaskRecordWatcher Watcher { get; }

        private Harness(WebApplication app, HubConnection connection, string tasksDir, TaskRecordWatcher watcher)
        {
            _app = app;
            _connection = connection;
            TasksDir = tasksDir;
            Watcher = watcher;
        }

        public static async Task<Harness> StartAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"grimoire-task-record-watcher-{Guid.NewGuid():N}");
            var tasksDir = Path.Combine(root, "wiki", "tasks");
            Directory.CreateDirectory(tasksDir);

            var resolvedPaths = new ResolvedGrimoirePaths(
                BaseDir: root,
                DataDir: root,
                ContentRoot: Path.Combine(root, "wiki"),
                PagesDir: Path.Combine(root, "wiki", "pages"),
                TasksDir: tasksDir,
                IndexPath: Path.Combine(root, "wiki", "index.md"),
                LogPath: Path.Combine(root, "wiki", "log.md"),
                RawOriginalsDir: Path.Combine(root, "raw", "originals"),
                RawSourcesDir: Path.Combine(root, "raw", "sources"),
                StateDbPath: Path.Combine(root, "operational-state.db"),
                SecretsFilePath: Path.Combine(root, ".env"),
                InstructionsDir: Path.Combine(root, "agents", "ingest"),
                SystemPromptPath: Path.Combine(root, "agents", "ingest", "system-prompt.md"),
                DefaultUserPromptPath: Path.Combine(root, "agents", "ingest", "default-user-prompt.md"),
                PolicyPath: Path.Combine(root, "agents", "ingest", "policy.json"),
                AgentWorkerPath: "unused",
                Locations: []);

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
                new TaskRecordWatcher(resolvedPaths, new IngestLifecyclePublisher(app.Services.GetRequiredService<IHubContext<IngestLifecycleHub>>())));

            connection.On<TaskRecordChangedEvent>("taskRecordChanged", e =>
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
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                while (DateTime.UtcNow < deadline)
                {
                    if (EventsFor(sentinelId).Count > seen)
                    {
                        return;
                    }

                    await Task.Delay(25);
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

        public List<TaskRecordChangedEvent> EventsFor(string taskId)
        {
            lock (_lock) { return [.. _received.Where(e => e.TaskId == taskId)]; }
        }

        public async Task<List<TaskRecordChangedEvent>> WaitForEventsAsync(string taskId, int expectedCount, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var matches = EventsFor(taskId);
                if (matches.Count >= expectedCount)
                {
                    return matches;
                }
                await Task.Delay(25);
            }

            throw new TimeoutException($"Expected {expectedCount} taskRecordChanged event(s) for '{taskId}', got {EventsFor(taskId).Count}.");
        }

        public async ValueTask DisposeAsync()
        {
            await Watcher.StopAsync(CancellationToken.None);
            Watcher.Dispose();
            await _connection.StopAsync();
            await _connection.DisposeAsync();
            await _app.StopAsync();
            await _app.DisposeAsync();
            try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(TasksDir))!, recursive: true); } catch { /* best-effort */ }
        }
    }
}
