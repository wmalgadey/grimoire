using Grimoire.AgentRuntime.Composition;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Host;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.AgentRuntime.Telemetry;
using Grimoire.LintAgent;
using Microsoft.Extensions.Logging;

// Composition root (ADR-013, T019 013-lint-agent US1): the Lint Agent Profile —
// identity, frozen telemetry identities, the full read/write tool set
// (list_files/read_file/write_file, unlike Query's pre-ADR-015 read-only shape),
// required instruction documents (system prompt only, no default-user-prompt — Lint
// takes no per-run user input at all), and ADR-004 model env-var names — plus the Lint
// intent hooks, running on the shared AgentHost template (specs/013-lint-agent plan.md).
var profile = new AgentProfile(
    AgentName: "lint",
    ServiceName: "Grimoire.LintAgent",
    ActivitySourceName: "Grimoire.LintAgent",
    MeterName: "Grimoire.LintAgent",
    RunSpanName: "lint_agent.run",
    CorrelationAttribute: "run_id",
    ToolRegistry: LintToolRegistry.Default,
    RequiredInstructionDocuments: new HashSet<InstructionDocument>
    {
        InstructionDocument.SystemPrompt,
    },
    ModelEnvVarNames: new ModelEnvVarNames("GRIMOIRE_LINT_MODEL", "GRIMOIRE_LINT_BASE_URL"));

using var telemetry = AgentTelemetryBootstrap.Build(profile.ServiceName, profile.ActivitySourceName, profile.MeterName);
var loggerFactory = telemetry.LoggerFactory;
var logger = loggerFactory.CreateLogger("Grimoire.LintAgent.Program");

var options = ReadCliOptions(args);

// Stdout is the NDJSON event channel (ADR-008); all logging goes to stderr/OTLP.
using var runEvents = new RunEventEmitter(Console.Out, options.RunId);

using var runSpan = LintAgentTracing.StartRunActivity(options.RunId);

var intent = new LintIntentHandler(profile, options, runEvents, loggerFactory, logger);

return await new AgentHost(profile).RunAsync(
    new AgentHostRun(
        WikiRoot: options.WikiRoot,
        SystemPromptPath: options.SystemPromptPath,
        PolicyPath: options.PolicyPath,
        HeartbeatSeconds: options.HeartbeatSeconds),
    runEvents,
    intent,
    CancellationToken.None);

static LintCliOptions ReadCliOptions(string[] args)
{
    var reader = new AgentArgumentReader(args);

    return new LintCliOptions(
        RunId: reader.GetRequired("--run-id"),
        WikiRoot: reader.GetRequired("--wiki-root"),
        SystemPromptPath: reader.GetRequired("--system-prompt-path"),
        PolicyPath: reader.GetRequired("--policy-path"),
        WriteLocksDir: reader.GetRequired("--write-locks-dir"),
        HeartbeatSeconds: reader.GetHeartbeatSeconds(),
        // T036: mirrors GetHeartbeatSeconds' own "parse or frozen default" shape — no
        // shared AgentArgumentReader helper, since only Lint has this option.
        ReviewWindowDays: int.TryParse(reader.GetOptional("--review-window-days"), out var parsedReviewWindow) && parsedReviewWindow > 0
            ? parsedReviewWindow
            : 90);
}

/// <summary>
/// The Lint intent hooks (ADR-013): a fixed kickoff message (Lint has no per-run user
/// input to scaffold) and the event-stream-only finalization — the Hub owns 100% of the
/// Findings Report's persistence from the NDJSON events this process emits on stdout
/// (mirrors Query's ADR-011 R3 shape). The code that differs because the intent differs.
/// </summary>
internal sealed class LintIntentHandler : IAgentIntentHandler
{
    private const string KickoffMessageTemplate =
        "Perform the wiki health check now: read the whole wiki, judge its condition " +
        "across all three Finding Categories, refresh any stale inbound-link counts you " +
        "find, and produce the Findings Report as your final message.\n\n" +
        "Effective Review Window for this run: {0} days (use this value instead of the " +
        "default stated in your system prompt).";

    private readonly AgentProfile _profile;
    private readonly LintCliOptions _options;
    private readonly RunEventEmitter _runEvents;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public LintIntentHandler(
        AgentProfile profile,
        LintCliOptions options,
        RunEventEmitter runEvents,
        ILoggerFactory loggerFactory,
        ILogger logger)
    {
        _profile = profile;
        _options = options;
        _runEvents = runEvents;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public Task PrepareAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task OnInstructionLoadFailureAsync(
        string documentKind, string documentPath, string reason, CancellationToken cancellationToken)
    {
        LintAgentLogEvents.LogInstructionsLoadFailed(_logger, _options.RunId, reason);
        return Task.CompletedTask;
    }

    public Task OnInstructionsLoadedAsync(LoadedInstructions instructions, CancellationToken cancellationToken)
    {
        LintAgentLogEvents.LogInstructionsLoaded(
            _logger,
            _options.RunId,
            instructions.SystemPrompt.Sha256,
            instructions.Policy.Identity.Version,
            instructions.Policy.Identity.Sha256);

        using (var loadSpan = LintAgentTracing.ActivitySource.StartActivity("lint_agent.load_instructions"))
        {
            loadSpan?.SetTag("run_id", _options.RunId);
            loadSpan?.SetTag("system_prompt_sha256", instructions.SystemPrompt.Sha256);
        }

        return Task.CompletedTask;
    }

    public async Task<int> ExecuteAsync(LoadedInstructions instructions, CancellationToken cancellationToken)
    {
        var modelClient = ModelClientFactory.Create(_loggerFactory, _profile.ModelEnvVarNames);

        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(
            instructions.Policy.Policy,
            journal,
            _options.WikiRoot,
            taskId: _options.RunId,
            registry: _profile.ToolRegistry,
            instrumentation: new LintToolCallInstrumentation(_loggerFactory.CreateLogger<GuardedToolExecutor>()),
            writeLocksDir: _options.WriteLocksDir);

        var loop = new AgentLoop(
            modelClient,
            executor,
            registry: _profile.ToolRegistry,
            instrumentation: new LintAgentLoopInstrumentation());

        var kickoffMessage = string.Format(
            System.Globalization.CultureInfo.InvariantCulture, KickoffMessageTemplate, _options.ReviewWindowDays);
        var initialConversation = new List<ConversationMessage>
        {
            new("user", kickoffMessage),
        };

        var result = await loop.RunAsync(
            instructions.SystemPrompt.Content,
            initialConversation,
            _options.RunId,
            CancellationToken.None);

        // T025 (015-lint-board-parity, ADR-018): split the agent's structured
        // proposed-actions block off its final message and carry the entries verbatim on
        // the terminal event — loop mechanics only; the actionability judgment and every
        // word of the proposals are agent-authored (Constitution Principle V).
        var (narrative, proposedActions) = ProposedActionsBlock.Extract(result.Narrative);

        // Mechanical reporting only (Constitution Principle V): the harness's own journal
        // (GuardedToolExecutor.TouchedPaths) already recorded every frontmatter-only write
        // this run performed — Lint's policy has exactly one write rule, so every touched
        // path is an inbound-link refresh. Reused via the same generic
        // RunCompletionMetadata.CreatedArtifacts/createdPages wire field Query uses for its
        // created-pages report (ADR-015) — no new event-channel field needed for this
        // narrower, agent-agnostic "paths this run wrote" fact.
        _runEvents.EmitCompleted(narrative, new RunCompletionMetadata(
            SystemPromptSha256: instructions.SystemPrompt.Sha256,
            PolicyPath: instructions.Policy.Identity.Path,
            PolicyVersion: instructions.Policy.Identity.Version,
            PolicySha256: instructions.Policy.Identity.Sha256,
            Model: modelClient.ModelId,
            TurnsUsed: result.TurnsUsed,
            DeniedActions: executor.Denials,
            CreatedArtifacts: executor.TouchedPaths,
            ProposedActions: proposedActions.Count > 0 ? proposedActions : null));
        return 0;
    }

    public Task<string> DescribeUnhandledFailureAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is AgentLoopCapException capEx)
            return Task.FromResult(capEx.Message);

        _logger.LogError(exception, "Lint agent failed for run {RunId}.", _options.RunId);
        return Task.FromResult(ErrorSanitizer.Sanitize(exception.Message, "Unknown lint error."));
    }
}
