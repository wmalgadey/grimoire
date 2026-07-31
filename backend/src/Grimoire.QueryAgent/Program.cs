using Grimoire.AgentRuntime.Composition;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Host;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.AgentRuntime.Telemetry;
using Grimoire.AgentRuntime.WikiLog;
using Grimoire.QueryAgent;
using Microsoft.Extensions.Logging;
using System.Text.Json;

// Composition root (ADR-013): the Query Agent Profile — identity, frozen telemetry
// identities, the read-only tool set, required instruction documents (system prompt
// only), and ADR-004 model env-var names — plus the Query intent hooks (stdin
// conversation scaffold), running on the shared AgentHost template. CLI surface,
// NDJSON event sequence (incl. answer_chunk streaming), and exit codes are
// byte-identical to the pre-consolidation host (FR-008; ADR-002/008/011 contracts).
var profile = new AgentProfile(
    AgentName: "query",
    ServiceName: "Grimoire.QueryAgent",
    ActivitySourceName: "Grimoire.QueryAgent",
    MeterName: "Grimoire.QueryAgent",
    RunSpanName: "query_agent.run",
    CorrelationAttribute: "turn_id",
    ToolRegistry: QueryToolRegistry.Default,
    RequiredInstructionDocuments: new HashSet<InstructionDocument>
    {
        InstructionDocument.SystemPrompt,
    },
    ModelEnvVarNames: new ModelEnvVarNames("GRIMOIRE_QUERY_MODEL", "GRIMOIRE_QUERY_BASE_URL"));

using var telemetry = AgentTelemetryBootstrap.Build(profile.ServiceName, profile.ActivitySourceName, profile.MeterName);
var loggerFactory = telemetry.LoggerFactory;
var logger = loggerFactory.CreateLogger("Grimoire.QueryAgent.Program");

var options = ReadCliOptions(args);

// Stdout is the NDJSON event channel (ADR-008, extended by ADR-011 with answer_chunk);
// all logging goes to stderr/OTLP — Query has no artifact write path, so there is
// nothing else this process writes anywhere (R3).
using var runEvents = new RunEventEmitter(Console.Out, options.TurnId);

using var runSpan = QueryAgentTracing.StartRunActivity(options.TurnId);

var conversationInput = await ReadConversationInputAsync();

// New — Query has no backstop before 014-wiki-storage-restructure (R5): most turns are
// routine lookups that touch nothing, so EnsureLogEntryAsync is only invoked when this
// turn actually created a Synthesis Page (see QueryIntentHandler.ExecuteAsync/
// DescribeUnhandledFailureAsync) — "for a completed action" (FR-010), not every turn.
var logAppender = new WikiLogAppender(
    QueryAgentTracing.ActivitySource, QueryAgentMetrics.Meter, loggerFactory.CreateLogger<WikiLogAppender>());

var intent = new QueryIntentHandler(profile, options, conversationInput, runEvents, logAppender, loggerFactory, logger);

return await new AgentHost(profile).RunAsync(
    new AgentHostRun(
        WikiRoot: options.WikiRoot,
        SystemPromptPath: options.SystemPromptPath,
        PolicyPath: options.PolicyPath,
        HeartbeatSeconds: options.HeartbeatSeconds),
    runEvents,
    intent,
    CancellationToken.None);

async Task<QueryConversationInput> ReadConversationInputAsync()
{
    var stdin = await Console.In.ReadToEndAsync();
    var parsed = JsonSerializer.Deserialize<QueryConversationInput>(
        stdin, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    return parsed ?? throw new InvalidOperationException(
        "Query conversation input on stdin was missing or not valid JSON.");
}

static QueryCliOptions ReadCliOptions(string[] args)
{
    var reader = new AgentArgumentReader(args);

    return new QueryCliOptions(
        TurnId: reader.GetRequired("--turn-id"),
        WikiRoot: reader.GetRequired("--wiki-root"),
        ContentRoot: reader.GetRequired("--content-root"),
        IndexPath: reader.GetRequired("--index-path"),
        LogPath: reader.GetRequired("--log-path"),
        SystemPromptPath: reader.GetRequired("--system-prompt-path"),
        PolicyPath: reader.GetRequired("--policy-path"),
        WriteLocksDir: reader.GetRequired("--write-locks-dir"),
        HeartbeatSeconds: reader.GetHeartbeatSeconds());
}

/// <summary>
/// The Query intent hooks (ADR-013): the stdin conversation scaffold and the
/// event-stream-only finalization (the Hub owns 100% of Query Run Artifact
/// persistence, ADR-011) — the code that differs because the intent differs.
/// Behavior is byte-identical to the pre-consolidation inline sequencing (FR-008).
/// </summary>
internal sealed class QueryIntentHandler : IAgentIntentHandler
{
    private readonly AgentProfile _profile;
    private readonly QueryCliOptions _options;
    private readonly QueryConversationInput _conversationInput;
    private readonly RunEventEmitter _runEvents;
    private readonly WikiLogAppender _logAppender;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    private GuardedToolExecutor? _executor;

    public QueryIntentHandler(
        AgentProfile profile,
        QueryCliOptions options,
        QueryConversationInput conversationInput,
        RunEventEmitter runEvents,
        WikiLogAppender logAppender,
        ILoggerFactory loggerFactory,
        ILogger logger)
    {
        _profile = profile;
        _options = options;
        _conversationInput = conversationInput;
        _runEvents = runEvents;
        _logAppender = logAppender;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public Task PrepareAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task OnInstructionLoadFailureAsync(
        string documentKind, string documentPath, string reason, CancellationToken cancellationToken)
    {
        QueryAgentLogEvents.LogInstructionsLoadFailed(_logger, _options.TurnId, reason);
        return Task.CompletedTask;
    }

    public Task OnInstructionsLoadedAsync(LoadedInstructions instructions, CancellationToken cancellationToken)
    {
        QueryAgentLogEvents.LogInstructionsLoaded(
            _logger,
            _options.TurnId,
            instructions.SystemPrompt.Sha256,
            instructions.Policy.Identity.Version,
            instructions.Policy.Identity.Sha256);

        using (var loadSpan = QueryAgentTracing.ActivitySource.StartActivity("query_agent.load_instructions"))
        {
            loadSpan?.SetTag("turn_id", _options.TurnId);
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
            taskId: _options.TurnId,
            registry: _profile.ToolRegistry,
            instrumentation: new QueryToolCallInstrumentation(_loggerFactory.CreateLogger<GuardedToolExecutor>()),
            writeLocksDir: _options.WriteLocksDir,
            logPath: _options.LogPath,
            indexPath: _options.IndexPath,
            activitySource: QueryAgentTracing.ActivitySource);
        _executor = executor;

        var loop = new AgentLoop(
            modelClient,
            executor,
            registry: _profile.ToolRegistry,
            instrumentation: new QueryAgentLoopInstrumentation(),
            onTextDelta: _runEvents.EmitAnswerChunk);

        var initialConversation = BuildInitialConversation(_conversationInput);

        var result = await loop.RunAsync(
            instructions.SystemPrompt.Content,
            initialConversation,
            _options.TurnId,
            CancellationToken.None);

        // 011-query-conversations (T027): the vestigial query_agent.finalize_artifact span
        // was removed with the artifact mechanism (ADR-014) — the Query agent never wrote
        // the artifact, and the Hub now records the turn into the Conversation Record. The
        // stdin/scaffold contract is untouched (ADR-012 fingerprints must not drift).

        // 014-wiki-storage-restructure (R5, FR-010): only turns that actually created a
        // Synthesis Page are "a completed action" needing a log.md entry — most turns are
        // routine lookups that touch nothing (system-prompt.md Step 6), so the backstop
        // must not fire unconditionally the way Ingest's does.
        if (executor.CreatedPaths.Count > 0)
        {
            await _logAppender.EnsureLogEntryAsync(
                _options.LogPath, "query", "completed", TruncatePrompt(_conversationInput.Prompt), _options.TurnId,
                forceAppend: false, CancellationToken.None);
        }

        _runEvents.EmitCompleted(result.Narrative, new RunCompletionMetadata(
            SystemPromptSha256: instructions.SystemPrompt.Sha256,
            PolicyPath: instructions.Policy.Identity.Path,
            PolicyVersion: instructions.Policy.Identity.Version,
            PolicySha256: instructions.Policy.Identity.Sha256,
            Model: modelClient.ModelId,
            TurnsUsed: result.TurnsUsed,
            DeniedActions: executor.Denials,
            CreatedArtifacts: executor.CreatedPaths));
        return 0;
    }

    public async Task<string> DescribeUnhandledFailureAsync(Exception exception, CancellationToken cancellationToken)
    {
        string reason;
        if (exception is AgentLoopCapException capEx)
        {
            reason = capEx.Message;
        }
        else
        {
            _logger.LogError(exception, "Query agent failed for turn {TurnId}.", _options.TurnId);
            reason = ErrorSanitizer.Sanitize(exception.Message, "Unknown query error.");
        }

        // 014-wiki-storage-restructure (R5, FR-010): a backstop is only owed once this
        // turn actually created a Synthesis Page — if the failure happened before any
        // write (the common case, since most turns write nothing), there is no
        // "completed action" for the fallback entry to describe.
        if (_executor is { CreatedPaths.Count: > 0 })
        {
            await _logAppender.EnsureLogEntryAsync(
                _options.LogPath, "query", "failed", TruncatePrompt(_conversationInput.Prompt), _options.TurnId,
                forceAppend: true, CancellationToken.None);
        }

        return reason;
    }

    private static string TruncatePrompt(string prompt)
    {
        const int maxLength = 120;
        return prompt.Length <= maxLength ? prompt : prompt[..maxLength] + "…";
    }

    private static List<ConversationMessage> BuildInitialConversation(QueryConversationInput input)
    {
        // Harness-owned message scaffold (ADR-007 pattern, ADR-011): each prior turn's
        // prompt/answer becomes a real user/assistant turn — not agent-editable content —
        // so the model resolves follow-up references (FR-009) the same way any multi-turn
        // conversation would. Interrupted turns' partial answers are included as-is.
        var conversation = new List<ConversationMessage>();

        foreach (var turn in input.PriorTurns ?? [])
        {
            conversation.Add(new ConversationMessage("user", turn.Prompt));
            if (!string.IsNullOrEmpty(turn.Answer))
            {
                conversation.Add(new ConversationMessage("assistant", turn.Answer));
            }
        }

        conversation.Add(new ConversationMessage("user", input.Prompt));
        return conversation;
    }
}
