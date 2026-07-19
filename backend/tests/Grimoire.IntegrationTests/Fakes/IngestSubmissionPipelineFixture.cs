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
/// location, with a swappable <see cref="IAgentProcessLauncher"/> fake and HTTP handler for the
/// URL fetcher, so pipeline tests can exercise the real submission + coordinator flow
/// hermetically (ADR-008).
/// </summary>
public sealed class IngestSubmissionPipelineFixture : IDisposable
{
    public string Root { get; }
    public ContentRootPaths ContentPaths { get; }
    public RawStoragePaths RawPaths { get; }
    public SourceArtifactStore SourceArtifactStore { get; }
    public KanbanBoardProjectionStore BoardStore { get; } = new();
    public FakeAgentProcessLauncher Launcher { get; }
    public OperationalStateRepository Repository { get; }
    public IngestRunCoordinator Coordinator { get; }
    public IngestSubmissionPipeline Pipeline { get; }
    public IngestSubmissionValidator Validator { get; } = new();
    public List<RealtimeLifecycleEvent> PublishedEvents { get; } = [];
    public List<(string Method, object? Payload, DateTimeOffset ReceivedAt)> PublishedActivity { get; } = [];
    public CaptureLogger<IngestSubmissionPipeline> Logger { get; } = new();
    public CaptureLogger<IngestRunCoordinator> CoordinatorLogger { get; } = new();
    public IngestLifecyclePublisher Publisher { get; private set; } = null!;

    /// <param name="contentPaths">
    /// Pre-resolved content-root paths (e.g. from <c>GrimoirePathResolver</c>, ADR-009) to
    /// wire the pipeline against instead of the fixture's own ad-hoc temp layout — lets
    /// path-configuration tests (specs/005-content-root-config) exercise the real pipeline
    /// under paths that came from actual resolution/validation. Required instruction files
    /// are still populated at these paths when they don't already exist.
    /// </param>
    /// <param name="rawPaths">Pre-resolved raw-storage paths, paired with <paramref name="contentPaths"/>.</param>
    /// <param name="root">Overrides the fixture's own temp root (used for <see cref="Dispose"/> cleanup scoping).</param>
    public IngestSubmissionPipelineFixture(
        FakeAgentProcessLauncher? launcher = null,
        HttpMessageHandler? urlFetchHandler = null,
        string markItDownExecutablePath = "markitdown",
        TimeSpan? livenessWindow = null,
        TimeProvider? timeProvider = null,
        ContentRootPaths? contentPaths = null,
        RawStoragePaths? rawPaths = null,
        string? root = null)
    {
        Root = root ?? Path.Combine(Path.GetTempPath(), $"grimoire-ingest-submission-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);

        if (contentPaths is not null)
        {
            ContentPaths = contentPaths;
            Directory.CreateDirectory(ContentPaths.PagesDir);
            Directory.CreateDirectory(ContentPaths.TasksDir);
            if (!File.Exists(ContentPaths.IndexPath)) File.WriteAllText(ContentPaths.IndexPath, "# Index\n");
            if (!File.Exists(ContentPaths.LogPath)) File.WriteAllText(ContentPaths.LogPath, string.Empty);
            Directory.CreateDirectory(Path.GetDirectoryName(ContentPaths.SystemPromptPath)!);
            if (!File.Exists(ContentPaths.SystemPromptPath)) File.WriteAllText(ContentPaths.SystemPromptPath, "# Test system prompt\nRules.\n");
            if (!File.Exists(ContentPaths.DefaultUserPromptPath)) File.WriteAllText(ContentPaths.DefaultUserPromptPath, "Please integrate the source.\n");
        }
        else
        {
            var contentRoot = Path.Combine(Root, "wiki");
            var instructionsDir = Path.Combine(Root, "agents", "ingest");
            ContentPaths = new ContentRootPaths(
                Root: contentRoot,
                PagesDir: Path.Combine(contentRoot, "pages"),
                TasksDir: Path.Combine(contentRoot, "tasks"),
                IndexPath: Path.Combine(contentRoot, "index.md"),
                LogPath: Path.Combine(contentRoot, "log.md"),
                SystemPromptPath: Path.Combine(instructionsDir, "system-prompt.md"),
                DefaultUserPromptPath: Path.Combine(instructionsDir, "default-user-prompt.md"),
                PolicyPath: Path.Combine(instructionsDir, "policy.json"));
            Directory.CreateDirectory(ContentPaths.PagesDir);
            Directory.CreateDirectory(ContentPaths.TasksDir);
            File.WriteAllText(ContentPaths.IndexPath, "# Index\n");
            File.WriteAllText(ContentPaths.LogPath, string.Empty);
            Directory.CreateDirectory(instructionsDir);
            File.WriteAllText(ContentPaths.SystemPromptPath, "# Test system prompt\nRules.\n");
            File.WriteAllText(ContentPaths.DefaultUserPromptPath, "Please integrate the source.\n");
        }

        RawPaths = rawPaths ?? new RawStoragePaths(
            OriginalsDir: Path.Combine(Root, "raw", "originals"),
            SourcesDir: Path.Combine(Root, "raw", "sources"));
        SourceArtifactStore = new SourceArtifactStore(RawPaths);

        var dbPath = Path.Combine(Root, "operational-state.db");
        Repository = new OperationalStateRepository(dbPath);
        Repository.InitializeAsync().GetAwaiter().GetResult();

        Launcher = launcher ?? new FakeAgentProcessLauncher();

        var hubContext = new RecordingHubContext(PublishedEvents, PublishedActivity);
        var publisher = new IngestLifecyclePublisher(hubContext);
        Publisher = publisher;

        Coordinator = new IngestRunCoordinator(
            Repository,
            Launcher,
            publisher,
            new HubTaskArtifactWriter(),
            ContentPaths,
            timeProvider,
            livenessWindow ?? TimeSpan.FromSeconds(60),
            CoordinatorLogger);
        Coordinator.InitializeAsync().GetAwaiter().GetResult();

        var httpClient = new HttpClient(urlFetchHandler ?? new NotFoundHandler());
        var urlFetcher = new UrlContentFetcher(httpClient);
        var converter = new MarkItDownConverter(new MarkItDownOptions(markItDownExecutablePath, TimeSpan.FromSeconds(30)));

        Pipeline = new IngestSubmissionPipeline(
            new HubTaskArtifactWriter(),
            SourceArtifactStore,
            converter,
            urlFetcher,
            publisher,
            Coordinator,
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
    private sealed class RecordingHubContext(
        List<RealtimeLifecycleEvent> sink,
        List<(string Method, object? Payload, DateTimeOffset ReceivedAt)> activitySink) : IHubContext<IngestLifecycleHub>
    {
        public IHubClients Clients { get; } = new RecordingClients(sink, activitySink);
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class RecordingClients(
            List<RealtimeLifecycleEvent> sink,
            List<(string Method, object? Payload, DateTimeOffset ReceivedAt)> activitySink) : IHubClients
        {
            public IClientProxy All { get; } = new RecordingClientProxy(sink, activitySink);
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => All;
            public IClientProxy Client(string connectionId) => All;
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => All;
            public IClientProxy Group(string groupName) => All;
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => All;
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => All;
            public IClientProxy User(string userId) => All;
            public IClientProxy Users(IReadOnlyList<string> userIds) => All;
        }

        private sealed class RecordingClientProxy(
            List<RealtimeLifecycleEvent> sink,
            List<(string Method, object? Payload, DateTimeOffset ReceivedAt)> activitySink) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                if (args.Length > 0 && args[0] is RealtimeLifecycleEvent lifecycleEvent)
                {
                    lock (sink) { sink.Add(lifecycleEvent); }
                }
                else if (args.Length > 0)
                {
                    lock (activitySink) { activitySink.Add((method, args[0], DateTimeOffset.UtcNow)); }
                }
                return Task.CompletedTask;
            }
        }
    }
}
