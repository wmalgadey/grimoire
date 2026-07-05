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

        // Build tool definitions for the SDK's custom Tool variant.
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
                var toolUseJson = JsonSerializer.Serialize(toolBlock);
                using var toolUseDoc = JsonDocument.Parse(toolUseJson);
                var root = toolUseDoc.RootElement;

                var toolUseId = root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString() ?? "unknown"
                    : "unknown";

                var toolName = root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                    ? nameProp.GetString() ?? "unknown"
                    : "unknown";

                var inputJson = root.TryGetProperty("input", out var inputProp)
                    ? inputProp.GetRawText()
                    : "{}";

                toolUseRequests.Add(new ToolUseRequest(
                    ToolUseId: toolUseId,
                    ToolName: toolName,
                    InputJson: inputJson));
            }
        }

        return new ModelTurn(
            AssistantText: assistantText,
            ToolUseRequests: toolUseRequests,
            StopReason: NormalizeStopReason(response.StopReason),
            InputTokens: (int)(response.Usage?.InputTokens ?? 0),
            OutputTokens: (int)(response.Usage?.OutputTokens ?? 0));
    }

    private static string NormalizeStopReason(object? stopReason)
    {
        var raw = stopReason?.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "end_turn";
        }

        // SDK enum values are PascalCase; normalize to protocol-style snake_case.
        if (string.Equals(raw, "EndTurn", StringComparison.OrdinalIgnoreCase))
        {
            return "end_turn";
        }

        if (string.Equals(raw, "ToolUse", StringComparison.OrdinalIgnoreCase))
        {
            return "tool_use";
        }

        if (string.Equals(raw, "MaxTokens", StringComparison.OrdinalIgnoreCase))
        {
            return "max_tokens";
        }

        if (string.Equals(raw, "PauseTurn", StringComparison.OrdinalIgnoreCase))
        {
            return "pause_turn";
        }

        if (string.Equals(raw, "StopSequence", StringComparison.OrdinalIgnoreCase))
        {
            return "stop_sequence";
        }

        if (string.Equals(raw, "Refusal", StringComparison.OrdinalIgnoreCase))
        {
            return "refusal";
        }

        return raw;
    }
}
