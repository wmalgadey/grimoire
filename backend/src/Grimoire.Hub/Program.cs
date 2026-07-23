using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.Conversion;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.IngestSubmission.Adapters.HttpFetch;
using Grimoire.Hub.IngestSubmission.Adapters.MarkItDown;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.Hub.Submission;
using Grimoire.Hub.TaskArtifact;
using Grimoire.Hub;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHubTelemetry();
builder.Services.AddSignalR();
builder.Services.AddHttpClient<IUrlContentFetcher, UrlContentFetcher>();
builder.Services.AddSingleton(sp => MarkItDownOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<IMarkdownConverter, MarkItDownConverter>();
builder.Services.AddSingleton<HubTaskArtifactWriter>();
builder.Services.AddSingleton<KanbanBoardProjectionStore>();

// ADR-009: every runtime location is composed in exactly one place, resolved before the
// host is built (no repository/project-structure discovery, FR-002/FR-003).
builder.Configuration.AddCommandLine(args, PathConfigurationSwitchMappingsFactory());

var pathOptions = new GrimoirePathOptions();
builder.Configuration.GetSection(GrimoirePathOptions.SectionName).Bind(pathOptions);

// FR-017: Query's own concurrency limit — read here alongside the other Grimoire:*
// settings; QueryRunCoordinator (008-query-agent) consumes it once it exists.
var queryConcurrencyOptions = new QueryConcurrencyOptions();
builder.Configuration.GetSection(QueryConcurrencyOptions.SectionName).Bind(queryConcurrencyOptions);
builder.Services.AddSingleton(queryConcurrencyOptions);

using (var bootstrapLoggerFactory = TelemetryExtensions.CreateBootstrapLoggerFactory())
{
    var pathLogger = bootstrapLoggerFactory.CreateLogger("Grimoire.Hub.Runtime.Paths");
    var resolvedPaths = GrimoirePathResolver.Resolve(pathOptions, builder.Configuration, pathLogger);

    var contentPaths = ContentRootPaths.FromResolved(resolvedPaths);
    var rawStoragePaths = RawStoragePaths.FromResolved(resolvedPaths);

    builder.Services.AddSingleton(resolvedPaths);
    builder.Services.AddSingleton(rawStoragePaths);
    builder.Services.AddSingleton<SourceArtifactStore>();
    builder.Services.AddSingleton<TaskRecordReadModel>();
    builder.Services.AddSingleton<IngestLifecyclePublisher>();
    builder.Services.AddHostedService<TaskRecordWatcher>();

    var repository = new OperationalStateRepository(resolvedPaths.StateDbPath);
    await repository.InitializeAsync();
    builder.Services.AddSingleton(repository);
    builder.Services.AddSingleton(contentPaths);
    builder.Services.AddSingleton(new LocalSecretsLoader(resolvedPaths.SecretsFilePath));
    builder.Services.AddSingleton<AgentProcessHost>(sp => new AgentProcessHost(sp.GetRequiredService<LocalSecretsLoader>(), resolvedPaths.AgentWorkerPath));
    builder.Services.AddSingleton<IAgentProcessLauncher>(sp => sp.GetRequiredService<AgentProcessHost>());
    builder.Services.AddSingleton<IngestRunCoordinator>(sp => new IngestRunCoordinator(
        sp.GetRequiredService<OperationalStateRepository>(),
        sp.GetRequiredService<IAgentProcessLauncher>(),
        sp.GetRequiredService<IngestLifecyclePublisher>(),
        sp.GetRequiredService<HubTaskArtifactWriter>(),
        sp.GetRequiredService<ContentRootPaths>(),
        logger: sp.GetRequiredService<ILogger<IngestRunCoordinator>>()));
    builder.Services.AddSingleton<IngestSubmissionValidator>();
    builder.Services.AddSingleton<IngestSubmissionPipeline>();

    var reconciler = new RestartReconciler(repository);
    await reconciler.ReconcileRunningTasksAsync(contentPaths.TasksDir, contentPaths.LogPath);

    if (args.Length > 0 && string.Equals(args[0], "submit-source", StringComparison.OrdinalIgnoreCase))
    {
        var sourcePath = ParseOption(args, "--path") ?? throw new ArgumentException("Missing --path option.");
        var sourceKind = ParseOption(args, "--source-kind") ?? "file";
        string? pastedText = null;
        if (sourceKind == "pasted_text")
        {
            pastedText = await Console.In.ReadToEndAsync();
        }

        var secretsLoader = new LocalSecretsLoader(resolvedPaths.SecretsFilePath);
        var processHost = new AgentProcessHost(secretsLoader, resolvedPaths.AgentWorkerPath);
        var service = new SubmissionService(repository, processHost);

        var taskId = await service.SubmitAsync(new SubmitSourceOptions(sourcePath, sourceKind, pastedText), contentPaths);
        Console.WriteLine($"Submitted ingest task: {taskId}");
        return;
    }
}

var app = builder.Build();

// FR-021: queued rows surviving a restart pause the queue until explicit user resume.
var coordinator = app.Services.GetRequiredService<IngestRunCoordinator>();
await coordinator.InitializeAsync();

app.MapGet("/", () => "Grimoire Hub");
app.MapHub<IngestLifecycleHub>("/hubs/ingest-lifecycle");
app.MapGroup("/api/ingest-submissions").MapIngestSubmissionEndpoints();
app.MapGroup("/api/ingest-queue").MapIngestQueueEndpoints();
app.Run();

static string? ParseOption(string[] args, string option)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

// ADR-009 command-line switches (contracts/path-configuration.md): mapped last so they
// win over environment/appsettings/defaults regardless of default-provider ordering.
static Dictionary<string, string> PathConfigurationSwitchMappingsFactory() => new(StringComparer.OrdinalIgnoreCase)
{
    ["--base-dir"] = "Grimoire:Paths:BaseDir",
    ["--data-dir"] = "Grimoire:Paths:DataDir",
    ["--content-root"] = "Grimoire:Paths:ContentRoot",
    ["--raw-dir"] = "Grimoire:Paths:RawDir",
    ["--state-db"] = "Grimoire:Paths:StateDb",
    ["--secrets-file"] = "Grimoire:Paths:SecretsFile",
    ["--instructions-dir"] = "Grimoire:Paths:InstructionsDir",
    ["--agent-worker"] = "Grimoire:Paths:AgentWorker",
};
