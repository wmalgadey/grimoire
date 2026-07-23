using Anthropic;
using Anthropic.Models.Messages;
using Grimoire.AgentRuntime.Core;
using Microsoft.Extensions.Logging;
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
        CancellationToken cancellationToken)
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

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = ModelId,
            MaxTokens = 8096,
            System = systemPrompt,
            Messages = messages,
            Tools = toolsList,
        }, cancellationToken: cancellationToken);

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
            OutputTokens: (int)(response.Usage?.OutputTokens ?? 0));
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
