using Anthropic;
using Anthropic.Models.Messages;
using System.Text.Json;

namespace Grimoire.IngestAgent.AgentCore;

/// <summary>
/// Production <see cref="IModelClient"/> over the Anthropic C# SDK Messages API.
/// Model ID comes from the <c>GRIMOIRE_INGEST_MODEL</c> environment variable
/// (default <c>claude-sonnet-4-6</c>).
/// </summary>
public sealed class AnthropicModelClient : IModelClient
{
    private const string DefaultModel = "claude-sonnet-4-6";

    private readonly AnthropicClient _client;

    // Tool definitions are static per run; cache the SDK conversion instead of
    // re-deserializing every schema on every turn.
    private IReadOnlyList<ToolDefinition>? _cachedToolSource;
    private List<ToolUnion>? _cachedTools;

    public AnthropicModelClient()
    {
        _client = new AnthropicClient();
        ModelId = Environment.GetEnvironmentVariable("GRIMOIRE_INGEST_MODEL") ?? DefaultModel;
    }

    public string ModelId { get; }

    public async Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversation,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        // Build messages from conversation history.
        var messages = new List<Anthropic.Models.Messages.MessageParam>();
        foreach (var msg in conversation)
        {
            messages.Add(new Anthropic.Models.Messages.MessageParam
            {
                Role = string.Equals(msg.Role, "user", StringComparison.OrdinalIgnoreCase)
                    ? Role.User
                    : Role.Assistant,
                Content = msg.Content,
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

}
