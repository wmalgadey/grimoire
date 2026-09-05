using Grimoire.Hub;
using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;
using Grimoire.Hub.ApiErrors;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.Conversion;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.IngestSubmission.Adapters.HttpFetch;
using Grimoire.Hub.IngestSubmission.Adapters.MarkItDown;
using Grimoire.Hub.IngestTaskArtifact;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.QueryConversations;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.QuerySubmission;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.RemediationTasks;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.AspNetCore.SignalR;

/// <summary>
/// The Hub's one composition point (018-hub-cli-commands, ADR-020 D1/D3): builds the same
/// service graph — telemetry, SignalR, all coordinators/publishers/stores, ADR-009 path
/// resolution, SQLite initialization, restart reconciliation, and the two post-Build
/// coordinator <c>InitializeAsync</c> calls — for both entry paths, without ever binding a
/// port. <c>Program.cs</c>'s web-host path calls this and then maps endpoints and calls
/// <c>app.Run()</c>; <c>Grimoire.Hub.Cli.HubCliApp</c> calls this, resolves command
/// instances from the returned app's <see cref="IServiceProvider"/>, and disposes the app
/// before the process exits (never runs it) — no port is ever bound on the CLI path.
///
/// Extracted verbatim from what was previously the top of <c>Program.cs</c> (017/018):
/// the only behavior change is the removal of the inline <c>submit-source</c> special
/// case, which 018 migrates to <c>Grimoire.Hub.Cli.IngestSubmitSourceCommand</c> — dispatched by
/// the same <c>CommandApp</c> as every other command instead of living inside this
/// composition step.
///
/// Deliberately declared in the GLOBAL namespace (no <c>namespace</c> statement), not
/// <c>Grimoire.Hub</c>: this is the second half of the composition root that ADR-020
/// describes as "the composition root (global-namespace <c>Program</c>)" — extracted out
/// of <c>Program.cs</c>'s top-level statements purely so <c>HubCliApp</c> can call it too,
/// with no change in architectural role. <c>Grimoire.ArchTests</c>' C5 rule
/// (<c>HexagonalPortsAdapterRuleTests.HubOrchestration_MustNotReferenceConcreteAdapterTypes</c>)
/// exempts the composition root "by construction" by filtering on the
/// <c>Grimoire.Hub</c>-prefixed namespace — a type with no namespace at all falls outside
/// that filter the same way <c>Program</c> itself already does, so this class legitimately
/// constructs concrete adapters (<see cref="AgentProcessHost"/>,
/// <see cref="MarkItDownConverter"/>, <see cref="UrlContentFetcher"/>) without requiring an
/// explicit rule carve-out — matching ADR-020's "C5 (existing): non-adapter namespaces
/// never reference concrete adapter types | existing rule, unchanged scope."
/// </summary>
internal static class HubHostComposition
{
    public static async Task<WebApplication> BuildAsync(string[] args)
    {
        // WebApplicationBuilder defaults ContentRootPath to the process working directory,
        // which the "prod"/"dev"/"proxy" launch profiles deliberately set to the repo root
        // (so GrimoirePathResolver's cwd-based DataDir/WikiDir defaults, a separate lookup
        // below, resolve correctly). That leaves appsettings.{Environment}.json looked up
        // at the repo root instead of next to Grimoire.Hub.dll, where it actually lives —
        // pin it explicitly so environment-specific settings load regardless of the
        // launching cwd (ADR-022: ProcessBaseDirectory's one remaining consumer).
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = GrimoirePathResolver.ProcessBaseDirectory,
        });
        builder.Services.AddHubTelemetry();
        // 024-api-error-presentation (ADR-026): the unhandled path answers in the same envelope
        // as every deliberate rejection. Registered here rather than in Program.cs, which ADR-023
        // reduced to a one-line pass-through — the web host boots through the CLI default command,
        // so this is the composition point both paths actually reach.
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiErrorExceptionHandler>();
        builder.Services.AddSignalR();
        builder.Services.AddHttpClient<IUrlContentFetcher, UrlContentFetcher>();
        builder.Services.AddSingleton(sp => MarkItDownOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
        builder.Services.AddSingleton<IMarkdownConverter, MarkItDownConverter>();
        builder.Services.AddSingleton<HubIngestTaskArtifactWriter>();
        // 023 T021: the board's human-readable label comes from the source-artifact manifest,
        // so the projection store is constructed with it (registered further down, once the
        // resolved paths exist).
        builder.Services.AddSingleton(sp => new IngestKanbanBoardProjectionStore(sp.GetRequiredService<IngestSourceArtifactStore>()));

        // ADR-022: every runtime location is composed in exactly one place, resolved before
        // the host is built (no repository/project-structure discovery, FR-002/FR-003). The
        // mandatory-configuration check (FR-005/SC-006) happens inside Resolve itself,
        // before any location is touched — fail fast, before the DI graph below is built.
        builder.Configuration.AddCommandLine(args, PathConfigurationSwitchMappingsFactory());

        var pathOptions = new GrimoirePathOptions();
        builder.Configuration.GetSection(GrimoirePathOptions.SectionName).Bind(pathOptions);

        // FR-017: Query's own concurrency limit — read here alongside the other Grimoire:*
        // settings; QueryRunCoordinator (008-query-agent) consumes it once it exists.
        var queryConcurrencyOptions = new QueryConcurrencyOptions();
        builder.Configuration.GetSection(QueryConcurrencyOptions.SectionName).Bind(queryConcurrencyOptions);
        builder.Services.AddSingleton(queryConcurrencyOptions);

        // T036 (013-lint-agent, US2): Lint's Review Window, read alongside the other
        // Grimoire:* settings (same binding convention as QueryConcurrencyOptions above);
        // LintRunCoordinator threads the effective value into each spawned run's kickoff
        // context.
        var lintReviewWindowOptions = new LintReviewWindowOptions();
        builder.Configuration.GetSection(LintReviewWindowOptions.SectionName).Bind(lintReviewWindowOptions);
        builder.Services.AddSingleton(lintReviewWindowOptions);

        using (var bootstrapLoggerFactory = TelemetryExtensions.CreateBootstrapLoggerFactory())
        {
            var pathLogger = bootstrapLoggerFactory.CreateLogger("Grimoire.Hub.Runtime.Paths");
            var resolvedPaths = GrimoirePathResolver.Resolve(pathOptions, builder.Configuration, pathLogger);

            var contentPaths = IngestContentPaths.FromResolved(resolvedPaths);
            var rawStoragePaths = IngestRawStoragePaths.FromResolved(resolvedPaths);

            builder.Services.AddSingleton(resolvedPaths);
            builder.Services.AddSingleton(rawStoragePaths);
            builder.Services.AddSingleton<IngestSourceArtifactStore>();
            builder.Services.AddSingleton<IngestTaskRecordReadModel>();
            builder.Services.AddHostedService<IngestTaskRecordWatcher>();

            var repository = new OperationalStateRepository(resolvedPaths.StateDbPath);
            await repository.InitializeAsync();
            builder.Services.AddSingleton(repository);

            // 023-task-ui-improvements T005 (ADR-025, ADR-021): the one place the Hub binds
            // a clock. Production gets the system clock; deterministic tests construct the
            // coordinator with a FakeTimeProvider so the reactivation backoff schedule runs
            // on virtual time instead of wall-clock waits. Registered explicitly (rather
            // than left to an optional-parameter default) so both consumers below — history
            // timestamps in the publisher, backoff scheduling in the coordinator — provably
            // share one clock.
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IngestLifecyclePublisher>(sp => new IngestLifecyclePublisher(
                sp.GetRequiredService<IHubContext<IngestLifecycleHub>>(),
                sp.GetRequiredService<ILogger<IngestLifecyclePublisher>>(),
                sp.GetRequiredService<OperationalStateRepository>(),
                sp.GetRequiredService<TimeProvider>()));
            builder.Services.AddSingleton(contentPaths);
            builder.Services.AddSingleton(new LocalSecretsLoader(resolvedPaths.SecretsFilePath));
            builder.Services.AddSingleton<AgentProcessHost>(sp => new AgentProcessHost(
                sp.GetRequiredService<LocalSecretsLoader>(), resolvedPaths.Ingest.WorkerPath, resolvedPaths.Query.WorkerPath,
                resolvedPaths.Lint.WorkerPath, sp.GetRequiredService<ILogger<AgentProcessHost>>()));
            builder.Services.AddSingleton<IAgentProcessLauncher>(sp => sp.GetRequiredService<AgentProcessHost>());
            builder.Services.AddSingleton<IngestRunCoordinator>(sp => new IngestRunCoordinator(
                sp.GetRequiredService<OperationalStateRepository>(),
                sp.GetRequiredService<IAgentProcessLauncher>(),
                sp.GetRequiredService<IngestLifecyclePublisher>(),
                sp.GetRequiredService<HubIngestTaskArtifactWriter>(),
                sp.GetRequiredService<IngestContentPaths>(),
                sp.GetRequiredService<ResolvedGrimoirePaths>(),
                sp.GetRequiredService<TimeProvider>(),
                logger: sp.GetRequiredService<ILogger<IngestRunCoordinator>>(),
                // 023 T045 (FR-003): the manifest the human-readable label is resolved from,
                // so the Hub's own restart/failure artifact writes mirror what the UI shows.
                sourceArtifactStore: sp.GetRequiredService<IngestSourceArtifactStore>()));
            builder.Services.AddSingleton<IngestSubmissionValidator>();
            builder.Services.AddSingleton<IngestSubmissionPipeline>();
            // 018-hub-cli-commands T010: IngestSubmitSourceCommand resolves this via DI instead
            // of constructing its own instance (unlike the retired inline Program.cs
            // special case) — same "same coordinators the HTTP endpoints use" model every
            // other command follows.
            builder.Services.AddSingleton<IngestSubmissionService>();

            // 008-query-agent: fully decoupled from Ingest's coordinator (no shared
            // lock/slot, ADR-011/SC-006) — its own SignalR channel, bounded-concurrency
            // dispatch, and (011, ADR-014) the Conversation Record store as both audit
            // trail and context source.
            builder.Services.AddSingleton<QueryLifecyclePublisher>(sp => new QueryLifecyclePublisher(
                sp.GetRequiredService<IHubContext<QueryLifecycleHub>>(),
                sp.GetRequiredService<ILogger<QueryLifecyclePublisher>>()));
            builder.Services.AddSingleton<QueryConversationRecordStore>(sp => new QueryConversationRecordStore(
                resolvedPaths,
                logger: sp.GetRequiredService<ILogger<QueryConversationRecordStore>>()));
            builder.Services.AddSingleton<QuerySubmissionValidator>();
            builder.Services.AddSingleton<QueryRunCoordinator>(sp => new QueryRunCoordinator(
                sp.GetRequiredService<IAgentProcessLauncher>(),
                sp.GetRequiredService<QueryLifecyclePublisher>(),
                sp.GetRequiredService<QueryConversationRecordStore>(),
                resolvedPaths,
                sp.GetRequiredService<QueryConcurrencyOptions>(),
                logger: sp.GetRequiredService<ILogger<QueryRunCoordinator>>()));

            // 013-lint-agent: immediate-rejection single-active-run dispatch (ADR-016),
            // fully decoupled from Ingest's and Query's coordinators — its own Findings
            // Report store as the run's sole persistent artifact (data-model.md "Lint Run"
            // note: no separate run record file).
            builder.Services.AddSingleton<LintFindingsReportStore>(sp => new LintFindingsReportStore(
                resolvedPaths, logger: sp.GetRequiredService<ILogger<LintFindingsReportStore>>()));
            // 015-lint-board-parity T011: lint's own board lifecycle channel, mirroring the
            // Ingest/Query publisher wiring above (research.md R1 — /hubs/ingest-lifecycle
            // is never touched, FR-015).
            builder.Services.AddSingleton<LintLifecyclePublisher>(sp => new LintLifecyclePublisher(
                sp.GetRequiredService<IHubContext<LintLifecycleHub>>(),
                sp.GetRequiredService<ILogger<LintLifecyclePublisher>>()));
            builder.Services.AddSingleton<LintRunCoordinator>(sp => new LintRunCoordinator(
                sp.GetRequiredService<IAgentProcessLauncher>(),
                sp.GetRequiredService<LintFindingsReportStore>(),
                resolvedPaths,
                reviewWindowOptions: sp.GetRequiredService<LintReviewWindowOptions>(),
                logger: sp.GetRequiredService<ILogger<LintRunCoordinator>>(),
                lifecyclePublisher: sp.GetRequiredService<LintLifecyclePublisher>(),
                // 015-lint-board-parity T017 (FR-004): unresolved remediation tasks block triggers.
                stateRepository: sp.GetRequiredService<OperationalStateRepository>(),
                // 015-lint-board-parity T022 (FR-007): proposal materialization gates completion.
                remediationRecordStore: sp.GetRequiredService<RemediationTaskRecordStore>(),
                remediationLifecyclePublisher: sp.GetRequiredService<RemediationLifecyclePublisher>()));

            // 015-lint-board-parity (ADR-018): remediation-task composition, mirroring the
            // Lint/Query pattern above — record store, lifecycle publisher (T023), read
            // endpoints (T024), and the FIFO execution coordinator and its
            // authorize/dismiss/withdraw transition endpoints (T032/T033).
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
            // 018-hub-cli-commands T021 (ADR-020): the authorize/dismiss/withdraw
            // transition service shared by RemediationTaskEndpoints and the CLI's
            // remediation-authorize/-dismiss/-withdraw commands (FR-005/SC-005).
            builder.Services.AddSingleton<RemediationTaskTransitionService>(sp => new RemediationTaskTransitionService(
                sp.GetRequiredService<OperationalStateRepository>(),
                sp.GetRequiredService<RemediationLifecyclePublisher>(),
                sp.GetRequiredService<RemediationRunCoordinator>(),
                sp.GetRequiredService<RemediationTaskRecordStore>(),
                sp.GetRequiredService<ILogger<RemediationLifecyclePublisher>>()));
            // 015-lint-board-parity T041/T042 (US5, FR-012): message turns dispatch
            // independently of execution — not authorization-gated, never touches the
            // task's execution state machine (ADR-018).
            builder.Services.AddSingleton<RemediationMessageTurnCoordinator>(sp => new RemediationMessageTurnCoordinator(
                sp.GetRequiredService<IAgentProcessLauncher>(),
                sp.GetRequiredService<RemediationLifecyclePublisher>(),
                sp.GetRequiredService<RemediationTaskRecordStore>(),
                resolvedPaths,
                logger: sp.GetRequiredService<ILogger<RemediationMessageTurnCoordinator>>()));

            var reconciler = new RestartReconciler(repository);
            await reconciler.ReconcileRunningIngestTasksAsync(contentPaths.TasksDir);
            // T034: Executing remediation rows with no live process are failed the same
            // way, before RemediationRunCoordinator.InitializeAsync (below, after
            // app.Build()) pauses the queue for any surviving Authorized rows.
            await reconciler.ReconcileRemediationTasksAsync(
                new RemediationTaskRecordStore(resolvedPaths));
        }

        var app = builder.Build();

        // FR-021: queued rows surviving a restart pause the queue until explicit user
        // resume. 018-hub-cli-commands: fresh-process semantics equal restart semantics
        // (research.md D1) — a CLI invocation runs this exactly like a freshly started Hub.
        var coordinator = app.Services.GetRequiredService<IngestRunCoordinator>();
        await coordinator.InitializeAsync();

        // 015-lint-board-parity T034: mirrors the ingest rule above — Authorized rows
        // surviving a restart pause the remediation execution queue (own flag) until
        // explicitly resumed.
        var remediationCoordinator = app.Services.GetRequiredService<RemediationRunCoordinator>();
        await remediationCoordinator.InitializeAsync();

        return app;
    }

    // ADR-009 command-line switches (contracts/path-configuration.md): mapped last so they
    // win over environment/appsettings/defaults regardless of default-provider ordering.
    // Derived from PathSwitchCatalog.All (single source of truth, Runtime/Paths/PathSwitchCatalog.cs).
    private static Dictionary<string, string> PathConfigurationSwitchMappingsFactory() =>
        PathSwitchCatalog.All.ToDictionary(s => s.Name, s => s.ConfigKey, StringComparer.OrdinalIgnoreCase);
}
