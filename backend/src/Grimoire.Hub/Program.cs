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

// 017-hub-help-usage (FR-001–FR-005): --help/-h must win over every other argument and
// exit before ANY startup side effect — including WebApplication.CreateBuilder(args),
// which itself doesn't fail on a bare invocation, but nothing after it (path resolution,
// secrets loading, SQLite init) may run for a help request. Checked first, ahead of
// everything else in this file, so --help works even with no data/ directory present.
if (args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase)
    || string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine(BuildUsageText());
    return;
}

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
    // 015-lint-board-parity T011: lint's own board lifecycle channel, mirroring the
    // Ingest/Query publisher wiring above (research.md R1 — /hubs/ingest-lifecycle is
    // never touched, FR-015).
    builder.Services.AddSingleton<LintLifecyclePublisher>(sp => new LintLifecyclePublisher(
        sp.GetRequiredService<IHubContext<LintLifecycleHub>>(),
        sp.GetRequiredService<ILogger<LintLifecyclePublisher>>()));
    builder.Services.AddSingleton<LintRunCoordinator>(sp => new LintRunCoordinator(
        sp.GetRequiredService<IAgentProcessLauncher>(),
        sp.GetRequiredService<FindingsReportStore>(),
        resolvedPaths,
        reviewWindowOptions: sp.GetRequiredService<LintReviewWindowOptions>(),
        logger: sp.GetRequiredService<ILogger<LintRunCoordinator>>(),
        lifecyclePublisher: sp.GetRequiredService<LintLifecyclePublisher>(),
        // 015-lint-board-parity T017 (FR-004): unresolved remediation tasks block triggers.
        stateRepository: sp.GetRequiredService<OperationalStateRepository>(),
        // 015-lint-board-parity T022 (FR-007): proposal materialization gates completion.
        remediationRecordStore: sp.GetRequiredService<RemediationTaskRecordStore>(),
        remediationLifecyclePublisher: sp.GetRequiredService<RemediationLifecyclePublisher>()));

    // 015-lint-board-parity (ADR-018): remediation-task composition, mirroring the Lint/
    // Query pattern above — record store, lifecycle publisher (T023), read endpoints
    // (T024), and now (T032/T033) the FIFO execution coordinator and its
    // authorize/dismiss/withdraw transition endpoints.
    builder.Services.AddSingleton<RemediationTaskRecordStore>(_ => new RemediationTaskRecordStore(resolvedPaths));
    builder.Services.AddSingleton<RemediationLifecyclePublisher>(sp => new RemediationLifecyclePublisher(
        sp.GetRequiredService<IHubContext<RemediationLifecycleHub>>(),
        sp.GetRequiredService<ILogger<RemediationLifecyclePublisher>>()));
    builder.Services.AddSingleton<RemediationRunCoordinator>(sp => new RemediationRunCoordinator(
        sp.GetRequiredService<OperationalStateRepository>(),
        sp.GetRequiredService<IAgentProcessLauncher>(),
        sp.GetRequiredService<RemediationLifecyclePublisher>(),
        sp.GetRequiredService<RemediationTaskRecordStore>(),
        resolvedPaths,
        logger: sp.GetRequiredService<ILogger<RemediationRunCoordinator>>()));
    // 015-lint-board-parity T041/T042 (US5, FR-012): message turns dispatch independently
    // of execution — not authorization-gated, never touches the task's execution state
    // machine (ADR-018).
    builder.Services.AddSingleton<RemediationMessageTurnCoordinator>(sp => new RemediationMessageTurnCoordinator(
        sp.GetRequiredService<IAgentProcessLauncher>(),
        sp.GetRequiredService<RemediationLifecyclePublisher>(),
        sp.GetRequiredService<RemediationTaskRecordStore>(),
        resolvedPaths,
        logger: sp.GetRequiredService<ILogger<RemediationMessageTurnCoordinator>>()));

    var reconciler = new RestartReconciler(repository);
    await reconciler.ReconcileRunningTasksAsync(contentPaths.TasksDir, contentPaths.LogPath);
    // T034: Executing remediation rows with no live process are failed the same way,
    // before RemediationRunCoordinator.InitializeAsync (below, after app.Build()) pauses
    // the queue for any surviving Authorized rows.
    await reconciler.ReconcileRemediationTasksAsync(
        new RemediationTaskRecordStore(resolvedPaths));

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

// 015-lint-board-parity T034: mirrors the ingest rule above — Authorized rows surviving
// a restart pause the remediation execution queue (own flag) until explicitly resumed.
var remediationCoordinator = app.Services.GetRequiredService<RemediationRunCoordinator>();
await remediationCoordinator.InitializeAsync();

app.MapGet("/", () => "Grimoire Hub");
app.MapHub<IngestLifecycleHub>("/hubs/ingest-lifecycle");
app.MapGroup("/api/ingest-submissions").MapIngestSubmissionEndpoints();
app.MapGroup("/api/ingest-queue").MapIngestQueueEndpoints();
app.MapHub<QueryLifecycleHub>("/hubs/query-lifecycle");
app.MapGroup("/api/query-conversations").MapQueryConversationEndpoints();
app.MapGroup("/api/query-turns").MapQueryTurnEndpoints();
app.MapHub<LintLifecycleHub>("/hubs/lint-lifecycle");
app.MapGroup("/api/lint-runs").MapLintRunEndpoints();
// 015-lint-board-parity T012: composite board initial state (contracts/lint-board-api.md).
app.MapGroup("/api/board").MapBoardEndpoints();
// 015-lint-board-parity T023/T024: remediation task lifecycle channel + read endpoints
// (contracts/remediation-lifecycle-events.md "Hub 2", contracts/remediation-task-api.md).
app.MapHub<RemediationLifecycleHub>("/hubs/remediation-lifecycle");
app.MapGroup("/api/remediation-tasks").MapRemediationTaskEndpoints();
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

// 017-hub-help-usage (FR-002, ADR-009): a short human-readable description per switch,
// keyed by the exact same switch strings as PathConfigurationSwitchMappingsFactory()
// above — kept next to it so the two are updated together. Switch NAMES themselves are
// never hand-duplicated: BuildUsageText() below iterates the factory's own keys, so a
// switch added there without a matching description here fails fast with a clear
// message instead of silently omitting it from --help output.
static Dictionary<string, string> PathConfigurationSwitchDescriptions() => new(StringComparer.OrdinalIgnoreCase)
{
    ["--base-dir"] = "Base directory all other relative Grimoire paths resolve against.",
    ["--data-dir"] = "Directory holding runtime data (state DB, secrets, agent instructions).",
    ["--content-root"] = "Root of the wiki content tree (pages, index, log).",
    ["--raw-dir"] = "Directory for raw/original source artifacts captured on ingest.",
    ["--state-db"] = "Path to the SQLite operational-state database file.",
    ["--secrets-file"] = "Path to the local secrets/.env file (e.g. provider API keys).",
    ["--instructions-dir"] = "Directory containing the Ingest agent's instruction files.",
    ["--agent-worker"] = "Path to the Ingest agent worker executable/DLL.",
    ["--query-instructions-dir"] = "Directory containing the Query agent's instruction files.",
    ["--conversations-dir"] = "Directory where Query conversation records are stored.",
    ["--query-agent-worker"] = "Path to the Query agent worker executable/DLL.",
    ["--write-locks-dir"] = "Directory used for cross-process write-coordination locks.",
    ["--findings-dir"] = "Directory where Lint findings reports are stored.",
    ["--lint-instructions-dir"] = "Directory containing the Lint agent's instruction files.",
    ["--lint-agent-worker"] = "Path to the Lint agent worker executable/DLL.",
    ["--remediation-tasks-dir"] = "Directory where remediation task records are stored.",
};

// 017-hub-help-usage (FR-001–FR-005): plain-text usage message printed for --help/-h.
// Command/switch NAMES are sourced from PathConfigurationSwitchMappingsFactory() (the
// single source of truth for ADR-009's switch vocabulary, also used to wire
// AddCommandLine above) so this text can never drift from what the Hub actually accepts.
static string BuildUsageText()
{
    var descriptions = PathConfigurationSwitchDescriptions();
    var lines = new List<string>
    {
        "Grimoire Hub — LLM-Wiki maintenance harness",
        string.Empty,
        "Usage:",
        "  Grimoire.Hub [--help|-h]",
        "  Grimoire.Hub [path options...]",
        "  Grimoire.Hub submit-source --path <path> --source-kind <kind> [path options...]",
        string.Empty,
        "Commands:",
        "  submit-source          Submit a source document for ingest into the wiki.",
        "    --path                Path to the source file to submit (required).",
        "    --source-kind         Kind of source: 'file' (default) or 'pasted_text' (read from stdin).",
        string.Empty,
        "Options:",
        "  --help, -h              Show this usage message and exit.",
    };

    var switchMappings = PathConfigurationSwitchMappingsFactory();
    // Computed (not hardcoded) so a longer switch name added later can never collide
    // with its own description column — the bug a fixed width would silently reproduce.
    var column = switchMappings.Keys.Max(name => name.Length) + 2;

    foreach (var (switchName, configKey) in switchMappings)
    {
        var description = descriptions.TryGetValue(switchName, out var value)
            ? value
            : throw new InvalidOperationException(
                $"Missing --help description for switch '{switchName}' (configuration key '{configKey}'). " +
                "Add an entry to PathConfigurationSwitchDescriptions() in Program.cs.");
        lines.Add($"  {switchName.PadRight(column)}{description}");
    }

    return string.Join(Environment.NewLine, lines);
}
