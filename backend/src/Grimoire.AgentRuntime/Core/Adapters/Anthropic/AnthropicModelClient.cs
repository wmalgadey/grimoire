using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Grimoire.AgentRuntime.Core;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Grimoire.AgentRuntime.Core.Adapters.Anthropic;

/// <summary>
/// Production <see cref="IModelClient"/> over the Anthropic C# SDK Messages API (ADR-010
/// P4, relocated to the shared runtime by ADR-011 C6). Model ID and base URL come from
/// caller-supplied environment variable names (default <c>GRIMOIRE_INGEST_MODEL</c> /
/// <c>GRIMOIRE_INGEST_BASE_URL</c>, preserving Grimoire.IngestAgent's existing behavior
/// unchanged); Grimoire.QueryAgent supplies its own <c>GRIMOIRE_QUERY_*</c> names so each
/// agent process's credential/model scoping (ADR-004) stays independent.
/// </summary>
public sealed class AnthropicModelClient : IModelClient
{
    /// <summary>
    /// #122: the per-request output ceiling, when the agent's own variable does not set
    /// one. It is an <em>enforced</em> cap the model is unaware of — hitting it truncates
    /// the response mid-thought, which comes back as <c>stop_reason: max_tokens</c> and
    /// costs another turn against the turn cap, and for a <c>write_file</c> carrying a
    /// large page the truncated body is still a syntactically valid tool call.
    /// <para>
    /// The value it replaces was the literal <c>8096</c>, which is not a round number in
    /// any base and reads as a typo for this one. It is deliberately left at the same
    /// order of magnitude: raising it is a question about what the configured model can
    /// actually emit, which is the tier decision in #117, not something to settle by
    /// widening a default underneath every agent at once. What changes here is that an
    /// operator who needs more can now say so per agent without a code change.
    /// </para>
    /// </summary>
    public const int DefaultMaxOutputTokens = 8192;

    /// <summary>
    /// #120: who acts on a retryable rejection, decided here rather than left to the SDK's
    /// unstated default. The <em>short</em> case — a burst 429 while the Hub dispatches
    /// several agents at once, a momentary 5xx — is absorbed inside the call by the SDK's
    /// own bounded retry, which is the only layer that can retry a single turn without
    /// re-running the whole task. Anything that outlives those attempts becomes a failed
    /// run carrying <see cref="ModelApiException.IsRetryable"/>, and re-entering the task
    /// is ADR-025's business, not the adapter's.
    /// <para>
    /// Bounded deliberately low: an OAuth credential answers a request for a model it is
    /// not entitled to with a 429 that no amount of waiting fixes (see deploy/README.md),
    /// so a generous retry budget would spend real time on a doomed request.
    /// </para>
    /// </summary>
    private const int MaxProviderRetries = 2;

    private readonly AnthropicClient _client;

    // Tool definitions are static per run; cache the SDK conversion instead of
    // re-deserializing every schema on every turn.
    private IReadOnlyList<ToolDefinition>? _cachedToolSource;
    private List<ToolUnion>? _cachedTools;

    private readonly long _maxOutputTokens;

    public AnthropicModelClient(
        ILogger<AnthropicModelClient> logger = null!,
        string modelEnvVar = "GRIMOIRE_INGEST_MODEL",
        string baseUrlEnvVar = "GRIMOIRE_INGEST_BASE_URL",
        string maxOutputTokensEnvVar = "GRIMOIRE_INGEST_MAX_OUTPUT_TOKENS")
    {
        var baseUrl = Environment.GetEnvironmentVariable(baseUrlEnvVar);

        _client = string.IsNullOrWhiteSpace(baseUrl)
            ? new AnthropicClient()
            {
                MaxRetries = MaxProviderRetries,
                Handlers = [new LoggingHandler(logger)],
            }
            : new AnthropicClient()
            {
                BaseUrl = baseUrl,
                MaxRetries = MaxProviderRetries,
                Handlers = [new LoggingHandler(logger)],
            };

        ModelId = ResolveModelId(modelEnvVar);
        _maxOutputTokens = ResolveMaxOutputTokens(maxOutputTokensEnvVar, logger);

        logger?.LogInformation(
            "AnthropicModelClient initialized with model {ModelId}, base URL {BaseUrl}, and max output tokens {MaxOutputTokens}.",
            ModelId, _client.BaseUrl, _maxOutputTokens);
    }

    /// <summary>
    /// #117 FR-001: the model id is resolved from configuration, and from nothing else.
    /// <para>
    /// This used to fall back to a <c>claude-opus-4-8</c> literal — a different tier than
    /// the one <c>.env-example</c> configures, silently, and per-agent: because the three
    /// variables do not inherit from one another, an operator who set only
    /// <c>GRIMOIRE_INGEST_MODEL</c> left Query and Lint running a model nobody chose, at a
    /// price nobody agreed to, with nothing in the logs saying so. A fallback that is
    /// wrong in a way no one can see is worse than no fallback, so there is none: an unset
    /// variable fails the run at composition, naming the variable to set.
    /// </para>
    /// </summary>
    private static string ResolveModelId(string modelEnvVar)
    {
        var configured = Environment.GetEnvironmentVariable(modelEnvVar);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{modelEnvVar} is not set. Each agent resolves its own model from its own " +
                "variable and inherits from no other (ADR-004); there is no code-level " +
                "default, so that an unset variable cannot silently run a model and a price " +
                "tier nobody chose. Set it in the Hub's .env file — see .env-example.");
        }

        return configured.Trim();
    }

    /// <summary>
    /// #122: the output ceiling, per agent, from the agent's own variable. A value that is
    /// not a positive integer falls back to <see cref="DefaultMaxOutputTokens"/> with a
    /// warning rather than failing the run — unlike the model id, a mistyped ceiling has a
    /// safe reading, and the run is still one the operator wants to happen.
    /// </summary>
    private static long ResolveMaxOutputTokens(
        string maxOutputTokensEnvVar, ILogger<AnthropicModelClient>? logger)
    {
        var raw = Environment.GetEnvironmentVariable(maxOutputTokensEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultMaxOutputTokens;
        }

        if (long.TryParse(raw, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        logger?.LogWarning(
            "{EnvVar} is set to '{Value}', which is not a positive integer; using {Default} instead.",
            maxOutputTokensEnvVar, raw, DefaultMaxOutputTokens);
        return DefaultMaxOutputTokens;
    }

    public string ModelId { get; }

    public async Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversation,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken,
        Action<string>? onTextDelta = null)
    {
        // Build messages from conversation history.
        var messages = new List<MessageParam>();
        foreach (var msg in conversation)
        {
            var contentBlocks = BuildContentBlocks(msg.ContentBlocks);
            messages.Add(new MessageParam
            {
                Role = string.Equals(msg.Role, "user", StringComparison.OrdinalIgnoreCase)
                    ? Role.User
                    : Role.Assistant,
                Content = new MessageParamContent(contentBlocks, null),
            });
        }

        if (!ReferenceEquals(_cachedToolSource, tools))
        {
            _cachedTools = BuildTools(tools);
            _cachedToolSource = tools;
        }

        var toolsList = _cachedTools!;

        var createParams = new MessageCreateParams
        {
            Model = ModelId,
            MaxTokens = _maxOutputTokens,
            System = systemPrompt,
            Messages = messages,
            Tools = toolsList,
        };

        return onTextDelta is null
            ? await NextTurnNonStreamingAsync(createParams, cancellationToken)
            : await NextTurnStreamingAsync(createParams, onTextDelta, cancellationToken);
    }

    private async Task<ModelTurn> NextTurnNonStreamingAsync(
        MessageCreateParams createParams, CancellationToken cancellationToken)
    {
        Message response;
        try
        {
            response = await _client.Messages.Create(createParams, cancellationToken: cancellationToken);
        }
        catch (AnthropicApiException ex)
        {
            throw TranslateProviderError(ex);
        }

        var toolUseRequests = new List<ToolUseRequest>();
        string? assistantText = null;

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                assistantText = (assistantText is null ? "" : assistantText) + textBlock.Text;
            }
            else if (block.TryPickToolUse(out var toolBlock))
            {
                toolUseRequests.Add(new ToolUseRequest(
                    ToolUseId: toolBlock.ID,
                    ToolName: toolBlock.Name,
                    InputJson: JsonSerializer.Serialize(toolBlock.Input)));
            }
        }

        return new ModelTurn(
            AssistantText: assistantText,
            ToolUseRequests: toolUseRequests,
            StopReason: ModelStopReasonContract.FromRawValue(response.StopReason),
            InputTokens: (int)(response.Usage?.InputTokens ?? 0),
            OutputTokens: (int)(response.Usage?.OutputTokens ?? 0),
            Refusal: MapRefusalDetails(response.StopDetails));
    }

    /// <summary>
    /// ADR-011 R2: consumes the Anthropic streaming Messages API, invoking
    /// <paramref name="onTextDelta"/> per text delta as it arrives so the first content
    /// can reach the caller well before the turn as a whole completes (SC-003), while
    /// still returning the same aggregated <see cref="ModelTurn"/> shape as the
    /// non-streaming path on completion.
    /// </summary>
    private async Task<ModelTurn> NextTurnStreamingAsync(
        MessageCreateParams createParams, Action<string> onTextDelta, CancellationToken cancellationToken)
    {
        var accumulator = new StreamingTurn();

        // A provider rejection on a streaming call surfaces while the sequence is being
        // advanced rather than at call time, so the whole consumption is covered.
        try
        {
            await foreach (var streamEvent in _client.Messages.CreateStreaming(createParams, cancellationToken))
            {
                accumulator.Apply(streamEvent, onTextDelta);
            }
        }
        catch (AnthropicApiException ex)
        {
            throw TranslateProviderError(ex);
        }

        return accumulator.ToModelTurn();
    }

    /// <summary>
    /// Per-turn accumulation of the streaming event sequence. Extracted from
    /// <see cref="NextTurnStreamingAsync"/> so the event dispatch and the
    /// translate-provider-errors wrapper around it stay separately readable (and each
    /// below the repository's complexity gate).
    /// </summary>
    private sealed class StreamingTurn
    {
        private readonly SortedDictionary<long, (string Id, string Name, StringBuilder Json)> _toolBlocksByIndex = new();
        private string? _assistantText;
        private ModelStopReason _stopReason = ModelStopReason.Unknown;
        private ModelRefusalDetails? _refusal;
        private int _inputTokens;
        private int _outputTokens;

        public void Apply(RawMessageStreamEvent streamEvent, Action<string> onTextDelta)
        {
            if (streamEvent.TryPickStart(out var start))
            {
                _inputTokens = (int)(start.Message.Usage?.InputTokens ?? 0);
            }
            else if (streamEvent.TryPickContentBlockStart(out var blockStart) &&
                     blockStart.ContentBlock.TryPickToolUse(out var toolUseStart))
            {
                _toolBlocksByIndex[blockStart.Index] = (toolUseStart.ID, toolUseStart.Name, new StringBuilder());
            }
            else if (streamEvent.TryPickContentBlockDelta(out var blockDelta))
            {
                ApplyContentBlockDelta(blockDelta, onTextDelta);
            }
            else if (streamEvent.TryPickDelta(out var messageDelta))
            {
                _stopReason = ModelStopReasonContract.FromRawValue(messageDelta.Delta.StopReason);
                _refusal = MapRefusalDetails(messageDelta.Delta.StopDetails);
                _outputTokens = (int)messageDelta.Usage.OutputTokens;
            }
        }

        private void ApplyContentBlockDelta(RawContentBlockDeltaEvent blockDelta, Action<string> onTextDelta)
        {
            if (blockDelta.Delta.TryPickText(out var textDelta) && !string.IsNullOrEmpty(textDelta.Text))
            {
                _assistantText = (_assistantText ?? string.Empty) + textDelta.Text;
                onTextDelta(textDelta.Text);
            }
            else if (blockDelta.Delta.TryPickInputJson(out var inputJsonDelta) &&
                     _toolBlocksByIndex.TryGetValue(blockDelta.Index, out var entry))
            {
                entry.Json.Append(inputJsonDelta.PartialJson);
            }
        }

        public ModelTurn ToModelTurn() => new(
            AssistantText: _assistantText,
            ToolUseRequests: _toolBlocksByIndex.Values
                .Select(t => new ToolUseRequest(
                    ToolUseId: t.Id,
                    ToolName: t.Name,
                    InputJson: t.Json.Length == 0 ? "{}" : t.Json.ToString()))
                .ToList(),
            StopReason: _stopReason,
            InputTokens: _inputTokens,
            OutputTokens: _outputTokens,
            Refusal: _refusal);
    }

    /// <summary>
    /// #119: maps the provider's <c>stop_details</c> — the <c>category</c>/<c>explanation</c>
    /// that accompany <c>stop_reason: "refusal"</c> — into the port's own
    /// <see cref="ModelRefusalDetails"/>, so the SDK type stays inside this namespace
    /// (ADR-010 containment) while the reason travels with the turn. Returns <c>null</c>
    /// for every non-refusal turn, which is what the provider sends there.
    /// </summary>
    private static ModelRefusalDetails? MapRefusalDetails(RefusalStopDetails? stopDetails)
    {
        if (stopDetails is null)
        {
            return null;
        }

        var category = stopDetails.Category?.Raw();
        return new ModelRefusalDetails(
            string.IsNullOrWhiteSpace(category) ? null : category,
            string.IsNullOrWhiteSpace(stopDetails.Explanation) ? null : stopDetails.Explanation);
    }

    /// <summary>
    /// 023 T051 (converge input; FR-006): translates a provider rejection into the
    /// harness-owned <see cref="ModelApiException"/>. Two things matter here.
    /// <para>
    /// First, the <em>message</em>: the SDK's own exception text is a bare status ("Status
    /// Code: BadRequest"), while the provider's explanation of what was actually wrong sits
    /// in the response body and used to be dropped. That explanation is what an operator
    /// needs on the card, the detail view, and the status history, so it is composed into
    /// the message here — once, at the single place the provider boundary is crossed.
    /// </para>
    /// <para>
    /// Second, the <em>type</em>: the SDK exception must not escape this namespace, or the
    /// Anthropic package leaks into orchestration code that ADR-010's containment rules keep
    /// it out of. The original is kept as the inner exception for diagnostics.
    /// </para>
    /// Applies to the Query and Lint agents too — they share this adapter.
    /// </summary>
    private static ModelApiException TranslateProviderError(AnthropicApiException exception)
    {
        var status = (int)exception.StatusCode;
        var (errorType, providerMessage) = ParseProviderError(exception.ResponseBody);
        var isRetryable = IsRetryable(exception);

        var text = $"Model API error {status}";
        var qualifiers = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(errorType))
        {
            qualifiers.Add(errorType);
        }
        // The classification goes ahead of the provider's own text, which the cap may
        // truncate — an operator has to be able to read "retryable" off a failure whose
        // explanation ran long.
        qualifiers.Add(isRetryable ? "retryable" : "terminal");
        text += $" ({string.Join(", ", qualifiers)})";

        if (!string.IsNullOrWhiteSpace(providerMessage))
        {
            text += $": {providerMessage}";
        }

        return new ModelApiException(
            OperatorFacingText.SingleLineCapped(text), status, errorType, isRetryable, exception);
    }

    /// <summary>
    /// #120: separates a rejected <em>request</em> from a rejecting <em>condition</em>.
    /// The SDK already types the distinction, so the typed exceptions are the primary
    /// signal; the status check behind them covers
    /// <see cref="AnthropicUnexpectedStatusCodeException"/>, which the SDK raises for
    /// statuses it has no dedicated type for.
    /// </summary>
    private static bool IsRetryable(AnthropicApiException exception)
        => exception is AnthropicRateLimitException or Anthropic5xxException
            || (int)exception.StatusCode == 429
            || (int)exception.StatusCode >= 500;

    /// <summary>
    /// Reads the provider's error envelope (<c>{"type":"error","error":{"type":…,"message":…}}</c>),
    /// tolerating a top-level <c>message</c> and any body that is empty, HTML, or otherwise
    /// not JSON — an unreadable body simply yields no detail, never an exception thrown from
    /// the error path itself.
    /// </summary>
    private static (string? ErrorType, string? Message) ParseProviderError(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object)
            {
                return (
                    error.TryGetProperty("type", out var nestedType) ? nestedType.GetString() : null,
                    error.TryGetProperty("message", out var nestedMessage) ? nestedMessage.GetString() : null);
            }

            return (
                document.RootElement.TryGetProperty("type", out var topType) ? topType.GetString() : null,
                document.RootElement.TryGetProperty("message", out var topMessage) ? topMessage.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static List<ToolUnion> BuildTools(IReadOnlyList<ToolDefinition> tools)
    {
        var toolsList = new List<ToolUnion>();
        foreach (var t in tools)
        {
            var schema = JsonSerializer.Deserialize<InputSchema>(t.InputSchemaJson)
                ?? throw new InvalidOperationException($"Invalid tool schema for '{t.Name}'.");

            toolsList.Add(new Tool
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = schema,
                // #127: strict tool use — the provider validates tool_use.input against the
                // schema before sending it, so a mis-shaped input never costs us a turn
                // against the turn cap and never produces a denial record that describes a
                // model slip rather than a policy decision. It constrains shape only:
                // whether the action is *allowed* remains GuardedToolExecutor's to decide,
                // deny-by-default, at the moment the tool is invoked (Principle V).
                Strict = true,
            });
        }

        return toolsList;
    }

    private static IReadOnlyList<ContentBlockParam> BuildContentBlocks(
        IReadOnlyList<ConversationContentBlock> blocks)
    {
        var contentBlocks = new List<ContentBlockParam>(blocks.Count);

        foreach (var block in blocks)
        {
            switch (block)
            {
                case ConversationTextBlock textBlock:
                    contentBlocks.Add(new ContentBlockParam(new TextBlockParam(textBlock.Text), null));
                    break;

                case ConversationToolUseBlock toolUseBlock:
                    var inputMap = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolUseBlock.InputJson)
                        ?? throw new InvalidOperationException(
                            $"Invalid tool_use input JSON for id '{toolUseBlock.ToolUseId}'.");

                    var anthropicToolUse = new ToolUseBlockParam
                    {
                        ID = toolUseBlock.ToolUseId,
                        Name = toolUseBlock.ToolName,
                        Input = inputMap,
                    };
                    contentBlocks.Add(new ContentBlockParam(anthropicToolUse, null));
                    break;

                case ConversationToolResultBlock toolResultBlock:
                    var anthropicToolResult = new ToolResultBlockParam(toolResultBlock.ToolUseId)
                    {
                        IsError = toolResultBlock.IsError,
                        Content = new ToolResultBlockParamContent(toolResultBlock.Content, null),
                    };
                    contentBlocks.Add(new ContentBlockParam(anthropicToolResult, null));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported conversation block type: {block.GetType().Name}.");
            }
        }

        return contentBlocks;
    }

    /// <summary>
    /// #123: per-request diagnostics for the provider call.
    /// <para>
    /// The <c>Information</c> line is the transaction — method, URL, status. The bodies are
    /// <c>Debug</c>, and are not even read unless <c>Debug</c> is enabled: at default levels
    /// this used to duplicate the system prompt, the whole conversation, every ingested
    /// source document, and every page body the agent wrote into the process log on every
    /// turn — and since the conversation is re-sent in full each turn, the log grew
    /// quadratically over a run that may take up to 50 of them. Ingested sources are
    /// untrusted external documents that may be private; ADR-004 scopes the credential
    /// carefully and the payload deserved the same care.
    /// </para>
    /// <para>
    /// This is a debugging aid, not the project's observability surface — that is ADR-005's
    /// spans, metrics, and structured events, none of which run through here. For a full,
    /// durable record of what crossed the port, <c>GRIMOIRE_MODEL_CAPTURE_PATH</c> is the
    /// purpose-built path (ADR-012).
    /// </para>
    /// </summary>
    private sealed class LoggingHandler : DelegatingHandler
    {
        private readonly ILogger<AnthropicModelClient>? _logger;

        public LoggingHandler(ILogger<AnthropicModelClient>? logger)
        {
            _logger = logger;
        }

        private bool BodiesWanted => _logger?.IsEnabled(LogLevel.Debug) == true;

        protected override HttpResponseMessage Send(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _logger?.LogInformation("Anthropic request: {Method} {Url}", request.Method, request.RequestUri);

            if (BodiesWanted && request.Content is not null)
            {
                LogRequestBody(request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            }

            var response = base.Send(request, cancellationToken);

            _logger?.LogInformation("Anthropic response: {StatusCode}", response.StatusCode);

            if (BodiesWanted && ShouldReadResponseBody(response))
            {
                LogResponseBody(response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            }

            return response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _logger?.LogInformation("Anthropic request: {Method} {Url}", request.Method, request.RequestUri);

            if (BodiesWanted && request.Content is not null)
            {
                LogRequestBody(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            var response = await base.SendAsync(request, cancellationToken);

            _logger?.LogInformation("Anthropic response: {StatusCode}", response.StatusCode);

            if (BodiesWanted && ShouldReadResponseBody(response))
            {
                LogResponseBody(await response.Content.ReadAsStringAsync(cancellationToken));
            }

            return response;
        }

        /// <summary>
        /// A streamed response is never read here. Buffering an <c>text/event-stream</c>
        /// body to a string waits for the stream to finish, which is precisely the delay
        /// Query's streaming exists to avoid (ADR-011 SC-003) — turning on a log level must
        /// not change how the product behaves. The turns are recoverable in full through
        /// <c>GRIMOIRE_MODEL_CAPTURE_PATH</c> instead.
        /// </summary>
        private static bool ShouldReadResponseBody(HttpResponseMessage response)
            => response.Content is not null
                && !string.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    "text/event-stream",
                    StringComparison.OrdinalIgnoreCase);

        private void LogRequestBody(string body)
            => _logger?.LogDebug("Anthropic request body: {RequestBody}", body);

        private void LogResponseBody(string body)
            => _logger?.LogDebug("Anthropic response body: {ResponseBody}", body);
    }
}
