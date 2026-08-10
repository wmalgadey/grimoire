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
// required instruction documents (system prompt only, no default-user-prompt — neither
// Lint invocation mode takes per-run input through ADR-007's default-user-prompt slot;
// each builds its own kickoff message locally, T035/T036 research.md R8), and ADR-004
// model env-var names — plus the per-mode intent hooks, running on the shared AgentHost
// template (specs/013-lint-agent plan.md, specs/015-lint-board-parity ADR-018).
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

// T035/T042 (015-lint-board-parity, ADR-018, research.md R8): one binary, several
// invocation modes, selected by `--mode` (default "lint-run" — the pre-existing, only
// mode before T035). `AgentProcessHost.StartRemediationProcess`/`StartMessageTurnProcess`
// spawn this binary with `--mode remediation-execution`/`--mode message-turn`
// respectively; this is what parses it.
var mode = new AgentArgumentReader(args).GetOptional("--mode") ?? "lint-run";

return mode switch
{
    "remediation-execution" => await RunRemediationExecutionAsync(args, profile, loggerFactory, logger),
    "message-turn" => await RunMessageTurnAsync(args, profile, loggerFactory, logger),
    _ => await RunLintRunAsync(args, profile, loggerFactory, logger),
};

static async Task<int> RunLintRunAsync(string[] args, AgentProfile profile, ILoggerFactory loggerFactory, ILogger logger)
{
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
            HeartbeatSeconds: options.HeartbeatSeconds,
            GrantedHarnessSurfaces: options.GrantedHarnessSurfaces),
        runEvents,
        intent,
        CancellationToken.None);
}

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
            : 90,
        GrantedHarnessSurfaces: reader.GetGrantedHarnessSurfaces());
}

/// <summary>
/// T035 (015-lint-board-parity, ADR-018): the remediation-execution mode's own run
/// sequence, mirroring <see cref="RunLintRunAsync"/>'s shape exactly (same AgentHost
/// template, same telemetry bootstrap) — only the CLI options record, the correlation id
/// (the remediation task id, not a lint run id), and the intent handler differ.
/// </summary>
static async Task<int> RunRemediationExecutionAsync(string[] args, AgentProfile profile, ILoggerFactory loggerFactory, ILogger logger)
{
    var options = ReadRemediationExecutionCliOptions(args);

    // Stdout is the NDJSON event channel (ADR-008); all logging goes to stderr/OTLP.
    using var runEvents = new RunEventEmitter(Console.Out, options.TaskId);

    using var runSpan = LintAgentTracing.StartRunActivity(options.TaskId);

    var intent = new RemediationExecutionIntentHandler(profile, options, runEvents, loggerFactory, logger);

    return await new AgentHost(profile).RunAsync(
        new AgentHostRun(
            WikiRoot: options.WikiRoot,
            SystemPromptPath: options.SystemPromptPath,
            PolicyPath: options.PolicyPath,
            HeartbeatSeconds: options.HeartbeatSeconds,
            GrantedHarnessSurfaces: options.GrantedHarnessSurfaces),
        runEvents,
        intent,
        CancellationToken.None);
}

static RemediationExecutionCliOptions ReadRemediationExecutionCliOptions(string[] args)
{
    var reader = new AgentArgumentReader(args);

    return new RemediationExecutionCliOptions(
        TaskId: reader.GetRequired("--task-id"),
        RunId: reader.GetRequired("--run-id"),
        WikiRoot: reader.GetRequired("--wiki-root"),
        SystemPromptPath: reader.GetRequired("--system-prompt-path"),
        PolicyPath: reader.GetRequired("--policy-path"),
        WriteLocksDir: reader.GetRequired("--write-locks-dir"),
        ProposalTitle: reader.GetRequired("--proposal-title"),
        ProposalDescription: reader.GetRequired("--proposal-description"),
        ProposalTargetPath: reader.GetOptional("--proposal-target-path"),
        AttachedContext: reader.GetOptional("--attached-context"),
        HeartbeatSeconds: reader.GetHeartbeatSeconds(),
        GrantedHarnessSurfaces: reader.GetGrantedHarnessSurfaces());
}

/// <summary>
/// T042 (015-lint-board-parity, ADR-018 "Message-turn mode"): the message-turn mode's own
/// run sequence — same AgentHost template as the other two modes, but the correlation id
/// is the remediation task id (shared with an execution run over that same task, since
/// both concern one task) and the intent handler is read-only by construction (see
/// <see cref="MessageTurnIntentHandler"/>).
/// </summary>
static async Task<int> RunMessageTurnAsync(string[] args, AgentProfile profile, ILoggerFactory loggerFactory, ILogger logger)
{
    var options = ReadMessageTurnCliOptions(args);
    var input = await ReadMessageTurnInputAsync();

    // Stdout is the NDJSON event channel (ADR-008); all logging goes to stderr/OTLP.
    using var runEvents = new RunEventEmitter(Console.Out, options.TaskId);

    using var runSpan = LintAgentTracing.StartRunActivity(options.TaskId);

    var intent = new MessageTurnIntentHandler(profile, options, input, runEvents, loggerFactory, logger);

    return await new AgentHost(profile).RunAsync(
        new AgentHostRun(
            WikiRoot: options.WikiRoot,
            SystemPromptPath: options.SystemPromptPath,
            PolicyPath: options.PolicyPath,
            HeartbeatSeconds: options.HeartbeatSeconds,
            GrantedHarnessSurfaces: options.GrantedHarnessSurfaces),
        runEvents,
        intent,
        CancellationToken.None);
}

static RemediationMessageTurnCliOptions ReadMessageTurnCliOptions(string[] args)
{
    var reader = new AgentArgumentReader(args);

    return new RemediationMessageTurnCliOptions(
        TaskId: reader.GetRequired("--task-id"),
        RunId: reader.GetRequired("--run-id"),
        WikiRoot: reader.GetRequired("--wiki-root"),
        SystemPromptPath: reader.GetRequired("--system-prompt-path"),
        PolicyPath: reader.GetRequired("--policy-path"),
        WriteLocksDir: reader.GetRequired("--write-locks-dir"),
        ProposalTitle: reader.GetRequired("--proposal-title"),
        ProposalDescription: reader.GetRequired("--proposal-description"),
        ProposalTargetPath: reader.GetOptional("--proposal-target-path"),
        AttachedContext: reader.GetOptional("--attached-context"),
        HeartbeatSeconds: reader.GetHeartbeatSeconds(),
        GrantedHarnessSurfaces: reader.GetGrantedHarnessSurfaces());
}

/// <summary>
/// T042: reads the new human message + prior-message context from stdin (mirrors
/// Grimoire.QueryAgent's <c>ReadConversationInputAsync</c> — both are the ADR-011
/// Query-turn shape).
/// </summary>
static async Task<RemediationMessageTurnInput> ReadMessageTurnInputAsync()
{
    var stdin = await Console.In.ReadToEndAsync();
    var parsed = System.Text.Json.JsonSerializer.Deserialize<RemediationMessageTurnInput>(
        stdin, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    return parsed ?? throw new InvalidOperationException(
        "Message-turn input on stdin was missing or not valid JSON.");
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
            ProposedActions: proposedActions.Count > 0 ? proposedActions : null,
            GrantedHarnessSurfaces: _options.GrantedHarnessSurfaces));
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

/// <summary>
/// The remediation-execution mode's intent hooks (T035, 015-lint-board-parity, ADR-018):
/// a kickoff message built from the authorized proposal (+ optional human-attached
/// context, US5) instead of Lint's fixed whole-wiki kickoff, and a terminal-event
/// derivation that layers the agent's re-verification verdict
/// (<see cref="RemediationOutcomeBlock"/>) on top of the harness's own mechanical facts
/// (what actually got written, what the guard actually denied) — never the other way
/// around. This keeps FR-018's judgment entirely in the agent's instructions
/// (data/agents/lint/system-prompt.md, T036) while the harness only ever reports what it
/// can observe directly (Constitution Principle V).
/// </summary>
internal sealed class RemediationExecutionIntentHandler : IAgentIntentHandler
{
    private const string KickoffMessageTemplate =
        "You are running in REMEDIATION EXECUTION MODE — see the \"Remediation Execution " +
        "Mode\" section of your instructions below; the whole-wiki lint-run instructions " +
        "above it do not apply to this run. Re-verify the following authorized " +
        "remediation action against the wiki's current content, then either apply it or " +
        "resolve it as no longer applicable, exactly as that section directs.\n\n" +
        "Title: {0}\n" +
        "Description: {1}\n" +
        "{2}" +
        "{3}";

    private readonly AgentProfile _profile;
    private readonly RemediationExecutionCliOptions _options;
    private readonly RunEventEmitter _runEvents;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public RemediationExecutionIntentHandler(
        AgentProfile profile,
        RemediationExecutionCliOptions options,
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
        LintAgentLogEvents.LogInstructionsLoadFailed(_logger, _options.TaskId, reason);
        return Task.CompletedTask;
    }

    public Task OnInstructionsLoadedAsync(LoadedInstructions instructions, CancellationToken cancellationToken)
    {
        LintAgentLogEvents.LogInstructionsLoaded(
            _logger,
            _options.TaskId,
            instructions.SystemPrompt.Sha256,
            instructions.Policy.Identity.Version,
            instructions.Policy.Identity.Sha256);

        using (var loadSpan = LintAgentTracing.ActivitySource.StartActivity("lint_agent.load_instructions"))
        {
            loadSpan?.SetTag("run_id", _options.TaskId);
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
            taskId: _options.TaskId,
            registry: _profile.ToolRegistry,
            instrumentation: new LintToolCallInstrumentation(_loggerFactory.CreateLogger<GuardedToolExecutor>()),
            writeLocksDir: _options.WriteLocksDir);

        var loop = new AgentLoop(
            modelClient,
            executor,
            registry: _profile.ToolRegistry,
            instrumentation: new LintAgentLoopInstrumentation());

        var targetPathLine = string.IsNullOrWhiteSpace(_options.ProposalTargetPath)
            ? string.Empty
            : $"Target page: {_options.ProposalTargetPath}\n";
        var attachedContextBlock = string.IsNullOrWhiteSpace(_options.AttachedContext)
            ? string.Empty
            : $"\nHuman-attached context (read this too before re-verifying):\n{_options.AttachedContext}\n";

        var kickoffMessage = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            KickoffMessageTemplate, _options.ProposalTitle, _options.ProposalDescription, targetPathLine, attachedContextBlock);
        var initialConversation = new List<ConversationMessage>
        {
            new("user", kickoffMessage),
        };

        var result = await loop.RunAsync(
            instructions.SystemPrompt.Content,
            initialConversation,
            _options.TaskId,
            CancellationToken.None);

        // T035/T036: split the agent's re-verification verdict off its final message —
        // loop mechanics only; the judgment itself is agent-authored (Principle V).
        var (narrative, outcomeEntry) = RemediationOutcomeBlock.Extract(result.Narrative);

        // Mechanical status derivation — the harness reports what it directly observed
        // through the guarded tool boundary and journal, using the agent's reported
        // verdict only for the one fact it alone can know (whether a re-verified-but-
        // unwritten proposal is moot vs. simply not yet attempted). Priority, highest
        // first:
        //  1. A write actually landed (GuardedToolExecutor.TouchedPaths) -> applied.
        //     Ground truth beats any self-report.
        //  2. No write landed, but the guard denied one or more attempts -> failed, with
        //     the guard's own recorded reason (research.md R7: an over-scope proposal
        //     fails at the guard — that is a failure, not a "not applicable" judgment;
        //     no new write mode is introduced to route around it).
        //  3. No write, no denial, and the agent's block reports not_applicable -> that
        //     verdict + reason, verbatim (FR-018 — the one path where the harness
        //     transports, never computes, an outcome).
        //  4. None of the above (no write, no denial, no usable block) -> failed with a
        //     harness safety-net reason: the run neither applied the fix nor reported why
        //     not, which is an incomplete run, not a judgment call for the harness to
        //     paper over.
        bool failed;
        string? remediationOutcome = null;
        string? reason;

        if (executor.TouchedPaths.Count > 0)
        {
            failed = false;
            reason = null;
        }
        else if (executor.Denials.Count > 0)
        {
            failed = true;
            var denialReasons = executor.Denials.Select(d => d.Reason).Distinct();
            reason = $"Remediation action denied by the write-scope guard: {string.Join("; ", denialReasons)}";
        }
        else if (outcomeEntry is { Outcome: RemediationOutcomeBlock.OutcomeNotApplicable })
        {
            failed = false;
            remediationOutcome = RemediationOutcomeBlock.OutcomeNotApplicable;
            reason = outcomeEntry.Reason ?? "Agent judged the proposal no longer applicable.";
        }
        else
        {
            failed = true;
            reason = "Remediation agent completed without applying the change or reporting why it was not applicable.";
        }

        var metadata = new RunCompletionMetadata(
            SystemPromptSha256: instructions.SystemPrompt.Sha256,
            PolicyPath: instructions.Policy.Identity.Path,
            PolicyVersion: instructions.Policy.Identity.Version,
            PolicySha256: instructions.Policy.Identity.Sha256,
            Model: modelClient.ModelId,
            TurnsUsed: result.TurnsUsed,
            DeniedActions: executor.Denials,
            CreatedArtifacts: executor.TouchedPaths,
            RemediationOutcome: remediationOutcome,
            GrantedHarnessSurfaces: _options.GrantedHarnessSurfaces);

        if (failed)
        {
            _runEvents.EmitFailed(reason!, metadata);
            return 1;
        }

        _runEvents.EmitCompleted(narrative, metadata, reason);
        return 0;
    }

    public Task<string> DescribeUnhandledFailureAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is AgentLoopCapException capEx)
            return Task.FromResult(capEx.Message);

        _logger.LogError(exception, "Remediation-execution agent failed for task {TaskId}.", _options.TaskId);
        return Task.FromResult(ErrorSanitizer.Sanitize(exception.Message, "Unknown remediation-execution error."));
    }
}

/// <summary>
/// The message-turn mode's intent hooks (T042, 015-lint-board-parity, ADR-018
/// "Message-turn mode": a bounded, read-only single exchange, ADR-011 Query-turn shape).
/// Structurally read-only: the loop is given the same tool registry as every other Lint
/// mode (write_file included — tool availability is never the enforcement boundary in
/// this codebase, see <c>QueryToolRegistry</c>'s own doc comment), but the guard it runs
/// under is <see cref="SafetyPolicy.WithNoWriteAccess"/>'s stripped clone of the loaded
/// policy — every write attempt is denied at the tool boundary (Constitution V), not
/// merely discouraged by instructions. The kickoff message carries the proposal, attached
/// context, and every prior message from the record (all Hub-sourced, R6
/// record-as-context); the reply is the agent's final narrative, verbatim, carried on the
/// terminal event's <c>text</c> field (no outcome block — a message turn has no
/// state-machine outcome to report).
/// </summary>
internal sealed class MessageTurnIntentHandler : IAgentIntentHandler
{
    private const string KickoffMessageTemplate =
        "You are running in MESSAGE-TURN MODE — see the \"Message-Turn Mode\" section of " +
        "your instructions below; the whole-wiki lint-run instructions above it do not " +
        "apply to this run, and you have no write access this turn (every write attempt " +
        "will be denied). A human is asking you about one specific proposed remediation " +
        "action — answer their question, grounded in the wiki's current content if you " +
        "need to look anything up.\n\n" +
        "Title: {0}\n" +
        "Description: {1}\n" +
        "{2}" +
        "{3}" +
        "{4}" +
        "Human's message: {5}\n";

    private readonly AgentProfile _profile;
    private readonly RemediationMessageTurnCliOptions _options;
    private readonly RemediationMessageTurnInput _input;
    private readonly RunEventEmitter _runEvents;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public MessageTurnIntentHandler(
        AgentProfile profile,
        RemediationMessageTurnCliOptions options,
        RemediationMessageTurnInput input,
        RunEventEmitter runEvents,
        ILoggerFactory loggerFactory,
        ILogger logger)
    {
        _profile = profile;
        _options = options;
        _input = input;
        _runEvents = runEvents;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public Task PrepareAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task OnInstructionLoadFailureAsync(
        string documentKind, string documentPath, string reason, CancellationToken cancellationToken)
    {
        LintAgentLogEvents.LogInstructionsLoadFailed(_logger, _options.TaskId, reason);
        return Task.CompletedTask;
    }

    public Task OnInstructionsLoadedAsync(LoadedInstructions instructions, CancellationToken cancellationToken)
    {
        LintAgentLogEvents.LogInstructionsLoaded(
            _logger,
            _options.TaskId,
            instructions.SystemPrompt.Sha256,
            instructions.Policy.Identity.Version,
            instructions.Policy.Identity.Sha256);

        using (var loadSpan = LintAgentTracing.ActivitySource.StartActivity("lint_agent.load_instructions"))
        {
            loadSpan?.SetTag("run_id", _options.TaskId);
            loadSpan?.SetTag("system_prompt_sha256", instructions.SystemPrompt.Sha256);
        }

        return Task.CompletedTask;
    }

    public async Task<int> ExecuteAsync(LoadedInstructions instructions, CancellationToken cancellationToken)
    {
        var modelClient = ModelClientFactory.Create(_loggerFactory, _profile.ModelEnvVarNames);

        var journal = new WriteJournal();
        // Constitution V: a real guardrail, not an instruction — every write attempt this
        // turn is denied at the tool boundary regardless of what the model does.
        var readOnlyPolicy = instructions.Policy.Policy.WithNoWriteAccess();
        var executor = new GuardedToolExecutor(
            readOnlyPolicy,
            journal,
            _options.WikiRoot,
            taskId: _options.TaskId,
            registry: _profile.ToolRegistry,
            instrumentation: new LintToolCallInstrumentation(_loggerFactory.CreateLogger<GuardedToolExecutor>()),
            writeLocksDir: _options.WriteLocksDir);

        var loop = new AgentLoop(
            modelClient,
            executor,
            registry: _profile.ToolRegistry,
            instrumentation: new LintAgentLoopInstrumentation());

        var targetPathLine = string.IsNullOrWhiteSpace(_options.ProposalTargetPath)
            ? string.Empty
            : $"Target page: {_options.ProposalTargetPath}\n";
        var attachedContextBlock = string.IsNullOrWhiteSpace(_options.AttachedContext)
            ? string.Empty
            : $"\nHuman-attached context:\n{_options.AttachedContext}\n";
        var priorMessagesBlock = BuildPriorMessagesBlock(_input.PriorMessages);

        var kickoffMessage = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            KickoffMessageTemplate,
            _options.ProposalTitle, _options.ProposalDescription, targetPathLine, attachedContextBlock, priorMessagesBlock,
            _input.Message);
        var initialConversation = new List<ConversationMessage>
        {
            new("user", kickoffMessage),
        };

        var result = await loop.RunAsync(
            instructions.SystemPrompt.Content,
            initialConversation,
            _options.TaskId,
            CancellationToken.None);

        var metadata = new RunCompletionMetadata(
            SystemPromptSha256: instructions.SystemPrompt.Sha256,
            PolicyPath: instructions.Policy.Identity.Path,
            PolicyVersion: instructions.Policy.Identity.Version,
            PolicySha256: instructions.Policy.Identity.Sha256,
            Model: modelClient.ModelId,
            TurnsUsed: result.TurnsUsed,
            DeniedActions: executor.Denials,
            CreatedArtifacts: executor.TouchedPaths,
            GrantedHarnessSurfaces: _options.GrantedHarnessSurfaces);

        // No outcome block (contract: "no new field: the agent's reply travels in the
        // existing text field of the completed event") — the whole narrative IS the reply.
        _runEvents.EmitCompleted(result.Narrative, metadata, text: result.Narrative);
        return 0;
    }

    private static string BuildPriorMessagesBlock(IReadOnlyList<RemediationMessageTurnPriorMessage>? priorMessages)
    {
        if (priorMessages is not { Count: > 0 })
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("\nPrior conversation about this task:\n");
        foreach (var message in priorMessages)
        {
            sb.Append(message.Sender == "agent" ? "You" : "Human").Append(": ").Append(message.Text).Append('\n');
        }

        return sb.ToString();
    }

    public Task<string> DescribeUnhandledFailureAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is AgentLoopCapException capEx)
            return Task.FromResult(capEx.Message);

        _logger.LogError(exception, "Message-turn agent failed for task {TaskId}.", _options.TaskId);
        return Task.FromResult(ErrorSanitizer.Sanitize(exception.Message, "Unknown message-turn error."));
    }
}
