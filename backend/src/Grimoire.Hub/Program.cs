using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.Conversion;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.IngestSubmission.Adapters.HttpFetch;
using Grimoire.Hub.IngestSubmission.Adapters.MarkItDown;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.OperationalState;
using Microsoft.AspNetCore.SignalR;
using Grimoire.Hub.QueryConversations;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.RemediationTasks;
using Grimoire.Hub.QuerySubmission;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.Hub.IngestTaskArtifact;
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

// T036 (013-lint-agent, US2): Lint's Review Window, read alongside the other Grimoire:*
// settings (same binding convention as QueryConcurrencyOptions above); LintRunCoordinator
// threads the effective value into each spawned run's kickoff context.
var lintReviewWindowOptions = new LintReviewWindowOptions();
builder.Configuration.GetSection(LintReviewWindowOptions.SectionName).Bind(lintReviewWindowOptions);
builder.Services.AddSingleton(lintReviewWindowOptions);

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
    builder.Services.AddSingleton<AgentProcessHost>(sp => new AgentProcessHost(
        sp.GetRequiredService<LocalSecretsLoader>(), resolvedPaths.AgentWorkerPath, resolvedPaths.QueryAgentWorkerPath,
        resolvedPaths.LintAgentWorkerPath));
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

    // 008-query-agent: fully decoupled from Ingest's coordinator (no shared lock/slot,
    // ADR-011/SC-006) — its own SignalR channel, bounded-concurrency dispatch, and (011,
    // ADR-014) the Conversation Record store as both audit trail and context source.
    builder.Services.AddSingleton<QueryLifecyclePublisher>(sp => new QueryLifecyclePublisher(
        sp.GetRequiredService<IHubContext<QueryLifecycleHub>>(),
        sp.GetRequiredService<ILogger<QueryLifecyclePublisher>>()));
    builder.Services.AddSingleton<ConversationRecordStore>(sp => new ConversationRecordStore(
        resolvedPaths,
        logger: sp.GetRequiredService<ILogger<ConversationRecordStore>>()));
    builder.Services.AddSingleton<QuerySubmissionValidator>();
    builder.Services.AddSingleton<QueryRunCoordinator>(sp => new QueryRunCoordinator(
        sp.GetRequiredService<IAgentProcessLauncher>(),
        sp.GetRequiredService<QueryLifecyclePublisher>(),
        sp.GetRequiredService<ConversationRecordStore>(),
        resolvedPaths,
        sp.GetRequiredService<QueryConcurrencyOptions>(),
        logger: sp.GetRequiredService<ILogger<QueryRunCoordinator>>()));

    // 013-lint-agent: immediate-rejection single-active-run dispatch (ADR-016), fully
    // decoupled from Ingest's and Query's coordinators — its own Findings Report store
    // as the run's sole persistent artifact (data-model.md "Lint Run" note: no separate
    // run record file).
    builder.Services.AddSingleton<FindingsReportStore>(sp => new FindingsReportStore(
        resolvedPaths, logger: sp.GetRequiredService<ILogger<FindingsReportStore>>()));
    builder.Services.AddSingleton<LintRunCoordinator>(sp => new LintRunCoordinator(
        sp.GetRequiredService<IAgentProcessLauncher>(),
        sp.GetRequiredService<FindingsReportStore>(),
        resolvedPaths,
        reviewWindowOptions: sp.GetRequiredService<LintReviewWindowOptions>(),
        logger: sp.GetRequiredService<ILogger<LintRunCoordinator>>()));

    // 015-lint-board-parity (ADR-018): remediation-task composition, mirroring the Lint/
    // Query pattern above — the append-only Remediation Task Record store for now; the
    // coordinator, lifecycle publisher, and endpoints join here as later phases add them
    // (T023/T032/T033).
    builder.Services.AddSingleton<RemediationTaskRecordStore>(_ => new RemediationTaskRecordStore(resolvedPaths));

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
app.MapHub<QueryLifecycleHub>("/hubs/query-lifecycle");
app.MapGroup("/api/query-conversations").MapQueryConversationEndpoints();
app.MapGroup("/api/query-turns").MapQueryTurnEndpoints();
app.MapGroup("/api/lint-runs").MapLintRunEndpoints();
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
    ["--query-instructions-dir"] = "Grimoire:Paths:QueryInstructionsDir",
    ["--conversations-dir"] = "Grimoire:Paths:ConversationsDir",
    ["--query-agent-worker"] = "Grimoire:Paths:QueryAgentWorker",
    ["--write-locks-dir"] = "Grimoire:Paths:WriteLocksDir",
    ["--findings-dir"] = "Grimoire:Paths:FindingsDir",
    ["--lint-instructions-dir"] = "Grimoire:Paths:LintInstructionsDir",
    ["--lint-agent-worker"] = "Grimoire:Paths:LintAgentWorker",
    ["--remediation-tasks-dir"] = "Grimoire:Paths:RemediationTasksDir",
};
