using Grimoire.AgentRuntime.Composition;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Host;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.AgentRuntime.Telemetry;
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

var intent = new QueryIntentHandler(profile, options, conversationInput, runEvents, loggerFactory, logger);

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
        PagesDir: reader.GetRequired("--pages-dir"),
        IndexPath: reader.GetRequired("--index-path"),
        LogPath: reader.GetRequired("--log-path"),
        SystemPromptPath: reader.GetRequired("--system-prompt-path"),
        PolicyPath: reader.GetRequired("--policy-path"),
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
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public QueryIntentHandler(
        AgentProfile profile,
        QueryCliOptions options,
        QueryConversationInput conversationInput,
        RunEventEmitter runEvents,
        ILoggerFactory loggerFactory,
        ILogger logger)
    {
        _profile = profile;
        _options = options;
        _conversationInput = conversationInput;
        _runEvents = runEvents;
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
            instrumentation: new QueryToolCallInstrumentation(_loggerFactory.CreateLogger<GuardedToolExecutor>()));

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

        using (var finalizeSpan = QueryAgentTracing.ActivitySource.StartActivity("query_agent.finalize_artifact"))
        {
            finalizeSpan?.SetTag("turn_id", _options.TurnId);
            finalizeSpan?.SetTag("outcome", "completed");
        }

        _runEvents.EmitCompleted(result.Narrative, new RunCompletionMetadata(
            SystemPromptSha256: instructions.SystemPrompt.Sha256,
            PolicyPath: instructions.Policy.Identity.Path,
            PolicyVersion: instructions.Policy.Identity.Version,
            PolicySha256: instructions.Policy.Identity.Sha256,
            Model: modelClient.ModelId,
            TurnsUsed: result.TurnsUsed,
            DeniedActions: executor.Denials));
        return 0;
    }

    public Task<string> DescribeUnhandledFailureAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is AgentLoopCapException capEx)
            return Task.FromResult(capEx.Message);

        _logger.LogError(exception, "Query agent failed for turn {TurnId}.", _options.TurnId);
        return Task.FromResult(ErrorSanitizer.Sanitize(exception.Message, "Unknown query error."));
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
