using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.Conversion;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.TaskArtifact;
using Microsoft.AspNetCore.SignalR;

namespace Grimoire.IntegrationTests.Fakes;

/// <summary>
/// Builds an <see cref="IngestSubmissionPipeline"/> wired to a temp content root and raw-storage
/// location, with a swappable <see cref="IIngestAgentDispatcher"/> fake and HTTP handler for the
/// URL fetcher, so US1/US2/US3 tests can exercise the real pipeline hermetically.
/// </summary>
public sealed class IngestSubmissionPipelineFixture : IDisposable
{
    public string Root { get; }
    public ContentRootPaths ContentPaths { get; }
    public RawStoragePaths RawPaths { get; }
    public SourceArtifactStore SourceArtifactStore { get; }
    public KanbanBoardProjectionStore BoardStore { get; } = new();
    public FakeIngestAgentDispatcher Dispatcher { get; }
    public IngestSubmissionPipeline Pipeline { get; }
    public IngestSubmissionValidator Validator { get; } = new();
    public List<RealtimeLifecycleEvent> PublishedEvents { get; } = [];
    public CaptureLogger<IngestSubmissionPipeline> Logger { get; } = new();

    public IngestSubmissionPipelineFixture(
        FakeIngestAgentDispatcher? dispatcher = null,
        HttpMessageHandler? urlFetchHandler = null,
        string markItDownExecutablePath = "markitdown")
    {
        Root = Path.Combine(Path.GetTempPath(), $"grimoire-ingest-submission-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);

        var wikiRoot = Path.Combine(Root, "wiki");
        ContentPaths = ContentRootPaths.Resolve(Root, "wiki");
        Directory.CreateDirectory(ContentPaths.PagesDir);
        Directory.CreateDirectory(ContentPaths.TasksDir);
        File.WriteAllText(ContentPaths.IndexPath, "# Index\n");
        File.WriteAllText(ContentPaths.LogPath, string.Empty);

        RawPaths = RawStoragePaths.Resolve(Root);
        SourceArtifactStore = new SourceArtifactStore(RawPaths);

        var dbPath = Path.Combine(Root, "operational-state.db");
        var repository = new OperationalStateRepository(dbPath);
        repository.InitializeAsync().GetAwaiter().GetResult();

        Dispatcher = dispatcher ?? new FakeIngestAgentDispatcher();

        var hubContext = new RecordingHubContext(PublishedEvents);
        var publisher = new IngestLifecyclePublisher(hubContext);

        var httpClient = new HttpClient(urlFetchHandler ?? new NotFoundHandler());
        var urlFetcher = new UrlContentFetcher(httpClient);
        var converter = new MarkItDownConverter(new MarkItDownOptions(markItDownExecutablePath, TimeSpan.FromSeconds(30)));

        Pipeline = new IngestSubmissionPipeline(
            new HubTaskArtifactWriter(),
            SourceArtifactStore,
            converter,
            urlFetcher,
            publisher,
            Dispatcher,
            new IngestRunGate(),
            repository,
            ContentPaths,
            Logger);
    }

    public string TaskArtifactPathFor(string taskId) => Path.Combine(ContentPaths.TasksDir, $"{taskId}.md");

    public async Task WaitForStatusAsync(string taskId, Func<string, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var path = TaskArtifactPathFor(taskId);
            if (File.Exists(path))
            {
                var frontmatter = TaskArtifactFrontmatter.TryParse(await File.ReadAllTextAsync(path));
                if (frontmatter is not null && predicate(frontmatter.Status))
                {
                    return;
                }
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Task '{taskId}' did not reach the expected status in time.");
    }

    /// <summary>
    /// Polls <see cref="PublishedEvents"/> (not the file) until a matching lifecycle event has
    /// actually been published — the file can reach a terminal status slightly before the
    /// corresponding realtime event is sent (TriggerAsync awaits the repository delete and file
    /// re-read in between), so tests asserting on published events must wait on the events, not
    /// the file, to avoid racing that gap.
    /// </summary>
    public async Task WaitForPublishedEventAsync(string taskId, Func<RealtimeLifecycleEvent, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            lock (PublishedEvents)
            {
                if (PublishedEvents.Any(e => e.TaskId == taskId && predicate(e)))
                {
                    return;
                }
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Task '{taskId}' did not publish the expected event in time.");
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    /// <summary>Minimal <see cref="IHubContext{IngestLifecycleHub}"/> that records published events instead of broadcasting over a real connection.</summary>
    private sealed class RecordingHubContext(List<RealtimeLifecycleEvent> sink) : IHubContext<IngestLifecycleHub>
    {
        public IHubClients Clients { get; } = new RecordingClients(sink);
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class RecordingClients(List<RealtimeLifecycleEvent> sink) : IHubClients
        {
            public IClientProxy All { get; } = new RecordingClientProxy(sink);
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => All;
            public IClientProxy Client(string connectionId) => All;
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => All;
            public IClientProxy Group(string groupName) => All;
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => All;
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => All;
            public IClientProxy User(string userId) => All;
            public IClientProxy Users(IReadOnlyList<string> userIds) => All;
        }

        private sealed class RecordingClientProxy(List<RealtimeLifecycleEvent> sink) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                if (args.Length > 0 && args[0] is RealtimeLifecycleEvent lifecycleEvent)
                {
                    lock (sink) { sink.Add(lifecycleEvent); }
                }
                return Task.CompletedTask;
            }
        }
    }
}
