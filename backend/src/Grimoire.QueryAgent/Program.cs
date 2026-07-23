using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Instructions;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.QueryAgent;
using System.Text.Json;
using System.Text.RegularExpressions;

var options = ParseArgs(args);

// Stdout is the NDJSON event channel (ADR-008, extended by ADR-011 with answer_chunk);
// all logging goes to stderr — Query has no artifact write path, so there is nothing
// else this process writes anywhere (R3).
using var runEvents = new RunEventEmitter(Console.Out, options.TurnId);

var conversationInput = await ReadConversationInputAsync();

try
{
    var promptLoader = new SystemPromptLoader();
    var systemPromptResult = await promptLoader.LoadAsync(options.SystemPromptPath, CancellationToken.None);
    if (systemPromptResult.IsSecond(out var systemPromptFailure))
    {
        runEvents.EmitFailed(systemPromptFailure.Reason);
        return 1;
    }
    systemPromptResult.IsFirst(out var loadedSystemPrompt);

    var policyLoader = new PolicyLoader(options.WikiRoot);
    var policyResult = await policyLoader.LoadAsync(options.PolicyPath, CancellationToken.None);
    if (policyResult.IsSecond(out var policyFailure))
    {
        runEvents.EmitFailed(policyFailure.Reason);
        return 1;
    }
    policyResult.IsFirst(out var loadedPolicy);

    // Event channel goes live once instructions and policy are loaded (contract: started
    // first, then heartbeats independent of model latency) — mirrors Ingest's sequencing.
    runEvents.EmitStarted();
    runEvents.StartHeartbeat(TimeSpan.FromSeconds(options.HeartbeatSeconds));

    var modelClient = new AnthropicModelClient(
        modelEnvVar: "GRIMOIRE_QUERY_MODEL",
        baseUrlEnvVar: "GRIMOIRE_QUERY_BASE_URL");

    var journal = new WriteJournal();
    var executor = new GuardedToolExecutor(
        loadedPolicy!.Policy,
        journal,
        options.WikiRoot,
        taskId: options.TurnId,
        registry: QueryToolRegistry.Default);

    var loop = new AgentLoop(
        modelClient,
        executor,
        registry: QueryToolRegistry.Default,
        onTextDelta: runEvents.EmitAnswerChunk);

    var initialConversation = BuildInitialConversation(conversationInput);

    var result = await loop.RunAsync(
        loadedSystemPrompt!.Content,
        initialConversation,
        options.TurnId,
        CancellationToken.None);

    runEvents.EmitCompleted(result.Narrative);
    return 0;
}
catch (AgentLoopCapException capEx)
{
    runEvents.EmitFailed(capEx.Message);
    return 1;
}
catch (Exception ex)
{
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
