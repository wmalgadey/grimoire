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
    private const string DefaultModel = "claude-opus-4-8";

    private readonly AnthropicClient _client;

    // Tool definitions are static per run; cache the SDK conversion instead of
    // re-deserializing every schema on every turn.
    private IReadOnlyList<ToolDefinition>? _cachedToolSource;
    private List<ToolUnion>? _cachedTools;

    public AnthropicModelClient(
        ILogger<AnthropicModelClient> logger = null!,
        string modelEnvVar = "GRIMOIRE_INGEST_MODEL",
        string baseUrlEnvVar = "GRIMOIRE_INGEST_BASE_URL")
    {
        var baseUrl = Environment.GetEnvironmentVariable(baseUrlEnvVar);

        _client = string.IsNullOrWhiteSpace(baseUrl)
            ? new AnthropicClient()
            {
                Handlers = [new LoggingHandler(logger)],
            }
            : new AnthropicClient()
            {
                BaseUrl = baseUrl,
                Handlers = [new LoggingHandler(logger)],
            };

        ModelId = Environment.GetEnvironmentVariable(modelEnvVar) ?? DefaultModel;

        logger?.LogInformation("AnthropicModelClient initialized with model {ModelId} and base URL {BaseUrl}.", ModelId, _client.BaseUrl);
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
            MaxTokens = 8096,
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

        var text = $"Model API error {status}";
        if (!string.IsNullOrWhiteSpace(errorType))
        {
            text += $" ({errorType})";
        }
        if (!string.IsNullOrWhiteSpace(providerMessage))
        {
            text += $": {providerMessage}";
        }

        return new ModelApiException(OperatorFacingText.SingleLineCapped(text), status, errorType, exception);
    }

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

    private class LoggingHandler : DelegatingHandler
    {
        private ILogger<AnthropicModelClient> _logger;

        public LoggingHandler(ILogger<AnthropicModelClient> logger)
        {
            this._logger = logger;
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _logger?.LogInformation("Anthropic request: {Method} {Url}", request.Method, request.RequestUri);

            if (request.Content != null)
            {
                var requestBody = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                _logger?.LogInformation("Anthropic request body: {RequestBody}", requestBody);
            }

            var response = base.Send(request, cancellationToken);

            _logger?.LogInformation("Anthropic response: {StatusCode}", response.StatusCode);

            if (response.Content != null)
            {
                var responseBody = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                _logger?.LogInformation("Anthropic response body: {ResponseBody}", responseBody);
            }

            return response;
        }

        override protected async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _logger?.LogInformation("Anthropic request: {Method} {Url}", request.Method, request.RequestUri);

            if (request.Content != null)
            {
                var requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
                _logger?.LogInformation("Anthropic request body: {RequestBody}", requestBody);
            }

            var response = await base.SendAsync(request, cancellationToken);

            _logger?.LogInformation("Anthropic response: {StatusCode}", response.StatusCode);

            if (response.Content != null)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger?.LogInformation("Anthropic response body: {ResponseBody}", responseBody);
            }

            return response;
        }
    }
}
