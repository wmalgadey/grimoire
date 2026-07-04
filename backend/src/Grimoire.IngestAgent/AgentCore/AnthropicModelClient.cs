using Anthropic;
using Anthropic.Models.Messages;
using System.Text.Json;

namespace Grimoire.IngestAgent.AgentCore;

/// <summary>
/// Production <see cref="IModelClient"/> over the Anthropic C# SDK Messages API.
/// Model ID comes from the <c>GRIMOIRE_INGEST_MODEL</c> environment variable
/// (default <c>claude-opus-4-8</c>).
/// </summary>
public sealed class AnthropicModelClient : IModelClient
{
    private const string DefaultModel = "claude-opus-4-8";

    private readonly AnthropicClient _client;
    private readonly string _modelId;

    public AnthropicModelClient()
    {
        _client = new AnthropicClient();
        _modelId = Environment.GetEnvironmentVariable("GRIMOIRE_INGEST_MODEL") ?? DefaultModel;
    }

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

        // Build tool definitions. Create Tool objects from JSON-serialized definitions.
        // The Anthropic SDK's Tool type uses discriminated unions internally,
        // so we construct them from the raw JSON schema dictionaries.
        var toolsList = new List<ToolUnion>();
        foreach (var t in tools)
        {
            using var jsonDoc = JsonDocument.Parse(t.InputSchemaJson);
            var toolDict = new Dictionary<string, object?>
            {
                { "type", "function" },
                { "name", t.Name },
                { "description", t.Description },
                { "input_schema", jsonDoc.RootElement.GetRawText() },
            };
            
            // Create Tool via reflection or use generic construction.
            // For now, use dynamic construction via the SDK's internal factory pattern.
            var toolJson = JsonSerializer.Serialize(toolDict);
            var tool = JsonSerializer.Deserialize<Tool>(toolJson);
            if (tool is not null)
            {
                toolsList.Add(tool);
            }
        }

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = _modelId,
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
                // ToolUseBlock is a union type with dynamic field access.
                // Extract id, name, and input via reflection.
                var toolId = GetToolUseFieldAsString(toolBlock, "id");
                var toolName = GetToolUseFieldAsString(toolBlock, "name");
                var toolInput = GetToolUseFieldAsString(toolBlock, "input");

                toolUseRequests.Add(new ToolUseRequest(
                    ToolUseId: toolId ?? "unknown",
                    ToolName: toolName ?? "unknown",
                    InputJson: toolInput ?? "{}"));
            }
        }

        return new ModelTurn(
            AssistantText: assistantText,
            ToolUseRequests: toolUseRequests,
            StopReason: response.StopReason?.ToString() ?? "end_turn",
            InputTokens: (int)(response.Usage?.InputTokens ?? 0),
            OutputTokens: (int)(response.Usage?.OutputTokens ?? 0));
    }

    /// <summary>
    /// Extract a field from ToolUseBlock as a string value.
    /// ToolUseBlock may store fields as JsonElement or other types.
    /// </summary>
    private static string? GetToolUseFieldAsString(ToolUseBlock toolBlock, string fieldName)
    {
        try
        {
            var prop = typeof(ToolUseBlock).GetProperty(
                fieldName,
                System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public);
            
            if (prop?.GetValue(toolBlock) is JsonElement jsonElem)
            {
                return jsonElem.GetRawText();
            }
            
            // Try accessing via indexer if it's a dictionary-like type
            var indexerProp = typeof(ToolUseBlock).GetProperty(
                "Item",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
            if (indexerProp != null)
            {
                var value = indexerProp.GetValue(toolBlock, new object[] { fieldName });
                if (value is JsonElement jsonElem2)
                {
                    return jsonElem2.GetRawText();
                }
                return value?.ToString();
            }
        }
        catch
        {
            // Fall through to null return
        }
        return null;
    }
}
