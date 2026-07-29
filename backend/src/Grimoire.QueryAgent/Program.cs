using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Grimoire.AgentRuntime.Core.Adapters.Replay;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Instructions;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.QueryAgent;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

using var telemetry = QueryAgentTelemetryBootstrap.Build();
var loggerFactory = telemetry.LoggerFactory;
var logger = loggerFactory.CreateLogger("Grimoire.QueryAgent.Program");

var options = ParseArgs(args);

// Stdout is the NDJSON event channel (ADR-008, extended by ADR-011 with answer_chunk);
// all logging goes to stderr/OTLP — Query has no artifact write path, so there is
// nothing else this process writes anywhere (R3).
using var runEvents = new RunEventEmitter(Console.Out, options.TurnId);

using var runSpan = QueryAgentTracing.StartRunActivity(options.TurnId);

var conversationInput = await ReadConversationInputAsync();

try
{
    var promptLoader = new SystemPromptLoader();
    var systemPromptResult = await promptLoader.LoadAsync(options.SystemPromptPath, CancellationToken.None);
    if (systemPromptResult.IsSecond(out var systemPromptFailure))
    {
        QueryAgentLogEvents.LogInstructionsLoadFailed(logger, options.TurnId, systemPromptFailure.Reason);
        runEvents.EmitFailed(systemPromptFailure.Reason);
        return 1;
    }
    systemPromptResult.IsFirst(out var loadedSystemPrompt);

    var policyLoader = new PolicyLoader(options.WikiRoot);
    var policyResult = await policyLoader.LoadAsync(options.PolicyPath, CancellationToken.None);
    if (policyResult.IsSecond(out var policyFailure))
    {
        QueryAgentLogEvents.LogInstructionsLoadFailed(logger, options.TurnId, policyFailure.Reason);
        runEvents.EmitFailed(policyFailure.Reason);
        return 1;
    }
    policyResult.IsFirst(out var loadedPolicy);

    QueryAgentLogEvents.LogInstructionsLoaded(
        logger, options.TurnId, loadedSystemPrompt!.Sha256, loadedPolicy!.Identity.Version, loadedPolicy.Identity.Sha256);

    using (var loadSpan = QueryAgentTracing.ActivitySource.StartActivity("query_agent.load_instructions"))
    {
        loadSpan?.SetTag("turn_id", options.TurnId);
        loadSpan?.SetTag("system_prompt_sha256", loadedSystemPrompt.Sha256);
    }

    // Event channel goes live once instructions and policy are loaded (contract: started
    // first, then heartbeats independent of model latency) — mirrors Ingest's sequencing.
    runEvents.EmitStarted();
    runEvents.StartHeartbeat(TimeSpan.FromSeconds(options.HeartbeatSeconds));

    var modelClient = CreateModelClient(loggerFactory);

    var journal = new WriteJournal();
    var executor = new GuardedToolExecutor(
        loadedPolicy.Policy,
        journal,
        options.WikiRoot,
        taskId: options.TurnId,
        registry: QueryToolRegistry.Default,
        instrumentation: new QueryToolCallInstrumentation(loggerFactory.CreateLogger<GuardedToolExecutor>()));

    var loop = new AgentLoop(
        modelClient,
        executor,
        registry: QueryToolRegistry.Default,
        instrumentation: new QueryAgentLoopInstrumentation(),
        onTextDelta: runEvents.EmitAnswerChunk);

    var initialConversation = BuildInitialConversation(conversationInput);

    var result = await loop.RunAsync(
        loadedSystemPrompt.Content,
        initialConversation,
        options.TurnId,
        CancellationToken.None);

    // 011-query-conversations (T027): the vestigial query_agent.finalize_artifact span
    // was removed with the artifact mechanism (ADR-014) — the Query agent never wrote
    // the artifact, and the Hub now records the turn into the Conversation Record. The
    // stdin/scaffold contract is untouched (ADR-012 fingerprints must not drift).

    runEvents.EmitCompleted(result.Narrative, new RunCompletionMetadata(
        SystemPromptSha256: loadedSystemPrompt.Sha256,
        PolicyPath: loadedPolicy.Identity.Path,
        PolicyVersion: loadedPolicy.Identity.Version,
        PolicySha256: loadedPolicy.Identity.Sha256,
        Model: modelClient.ModelId,
        TurnsUsed: result.TurnsUsed,
        DeniedActions: executor.Denials));
    return 0;
}
catch (AgentLoopCapException capEx)
{
    runEvents.EmitFailed(capEx.Message);
    return 1;
}
catch (Exception ex)
{
    logger.LogError(ex, "Query agent failed for turn {TurnId}.", options.TurnId);
    runEvents.EmitFailed(SanitizeErrorText(ex.Message));
    return 1;
}

async Task<QueryConversationInput> ReadConversationInputAsync()
{
    var stdin = await Console.In.ReadToEndAsync();
    var parsed = JsonSerializer.Deserialize<QueryConversationInput>(
        stdin, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    return parsed ?? throw new InvalidOperationException(
        "Query conversation input on stdin was missing or not valid JSON.");
}

static List<ConversationMessage> BuildInitialConversation(QueryConversationInput input)
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

// Composition-root model-adapter selection (ADR-012, T095 of 008-query-agent): mirrors
// Grimoire.IngestAgent/Program.cs's CreateModelClient — GRIMOIRE_MODEL_REPLAY_PATH serves
// a recording with no credential read; GRIMOIRE_MODEL_CAPTURE_PATH wraps the live
// adapter in the turn-capture decorator; both set is a fail-fast configuration error;
// neither preserves production behavior unchanged (still reads GRIMOIRE_QUERY_MODEL/
// GRIMOIRE_QUERY_BASE_URL, independent of Ingest's env vars, per ADR-004).
static IModelClient CreateModelClient(ILoggerFactory loggerFactory)
{
    var replayPath = Environment.GetEnvironmentVariable("GRIMOIRE_MODEL_REPLAY_PATH");
    var capturePath = Environment.GetEnvironmentVariable("GRIMOIRE_MODEL_CAPTURE_PATH");

    if (!string.IsNullOrWhiteSpace(replayPath) && !string.IsNullOrWhiteSpace(capturePath))
    {
        throw new InvalidOperationException(
            "Both GRIMOIRE_MODEL_REPLAY_PATH and GRIMOIRE_MODEL_CAPTURE_PATH are set. " +
            "Configure at most one of replay/capture mode (ADR-012); production leaves both unset.");
    }

    if (!string.IsNullOrWhiteSpace(replayPath))
    {
        return new ReplayModelClient(replayPath);
    }

    var liveClient = new AnthropicModelClient(
        loggerFactory.CreateLogger<AnthropicModelClient>(),
        modelEnvVar: "GRIMOIRE_QUERY_MODEL",
        baseUrlEnvVar: "GRIMOIRE_QUERY_BASE_URL");
    return string.IsNullOrWhiteSpace(capturePath)
        ? liveClient
        : new TurnCaptureModelClient(liveClient, capturePath);
}

static string SanitizeErrorText(string message)
{
    if (string.IsNullOrWhiteSpace(message))
        return "Unknown query error.";

    var sanitized = message;
    var envAuthToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
    if (!string.IsNullOrWhiteSpace(envAuthToken))
        sanitized = sanitized.Replace(envAuthToken, "[REDACTED]", StringComparison.Ordinal);

    sanitized = Regex.Replace(sanitized, "sk-ant-[A-Za-z0-9_-]+", "[REDACTED]",
        RegexOptions.CultureInvariant);
    return sanitized;
}

static QueryCliOptions ParseArgs(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < args.Length - 1; i += 2)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal))
            options[args[i]] = args[i + 1];
    }

    string GetRequired(string name)
        => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required argument {name}");

    string? GetOptional(string name)
        => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    var heartbeatSeconds = int.TryParse(GetOptional("--heartbeat-seconds"), out var parsedHeartbeat) && parsedHeartbeat > 0
        ? parsedHeartbeat
        : 10;

    return new QueryCliOptions(
        TurnId: GetRequired("--turn-id"),
        WikiRoot: GetRequired("--wiki-root"),
        PagesDir: GetRequired("--pages-dir"),
        IndexPath: GetRequired("--index-path"),
        LogPath: GetRequired("--log-path"),
        SystemPromptPath: GetRequired("--system-prompt-path"),
        PolicyPath: GetRequired("--policy-path"),
        HeartbeatSeconds: heartbeatSeconds);
}
