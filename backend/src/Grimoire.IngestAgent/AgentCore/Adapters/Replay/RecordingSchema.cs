using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grimoire.IngestAgent.AgentCore.Adapters.Replay;

/// <summary>
/// One conversation message of a recorded model request, stored as a content hash
/// (requests are fingerprints for replay matching; responses are stored verbatim).
/// Contract: specs/009-agent-eval-replay/contracts/recording-format.md.
/// </summary>
public sealed record RecordedMessage(string Role, string ContentSha256);

/// <summary>One tool_use returned by the model in a recorded turn (stored verbatim).</summary>
public sealed record RecordedToolUse(string ToolUseId, string ToolName, string InputJson);

/// <summary>One captured model turn: request fingerprints + verbatim response.</summary>
public sealed record RecordedTurn(
    int Turn,
    string SystemPromptSha256,
    IReadOnlyList<RecordedMessage> Conversation,
    IReadOnlyList<string> ToolNames,
    string StopReason,
    IReadOnlyList<RecordedToolUse> ToolUses,
    string? AssistantText,
    int InputTokens,
    int OutputTokens);

/// <summary>A recorded LLM-judge verdict, replayed verbatim (judge-scored scenarios only).</summary>
public sealed record JudgeVerdict(string JudgePromptSha256, string Verdict, string? Rationale);

/// <summary>The captured run's scored outcome, kept for replay-determinism cross-checks.</summary>
public sealed record RecordedOutcome(string Status, IReadOnlyDictionary<string, bool>? Checks);

/// <summary>
/// One sample recording (`sample-NN.json`): the full ordered interaction of one genuine
/// agent run, plus judge verdicts and captured outcome added by the eval runner.
/// </summary>
public sealed record RecordedSample(
    int SchemaVersion,
    int Sample,
    string TaskId,
    string Model,
    IReadOnlyList<RecordedTurn> Turns,
    IReadOnlyList<JudgeVerdict>? JudgeVerdicts,
    RecordedOutcome? Outcome);

/// <summary>
/// Serialization + canonical hashing shared by the capture decorator, the replay
/// adapter, and the eval runner. Hashes are `sha256:&lt;lowercase hex&gt;` over UTF-8.
/// </summary>
public static class RecordingSerialization
{
    public const int CurrentSchemaVersion = 1;

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Hash(string content)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>
    /// Canonical hash of one conversation message: role plus a stable rendering of every
    /// content block, so capture and replay agree on request identity (research.md R2).
    /// </summary>
    public static string HashMessage(ConversationMessage message)
    {
        var builder = new StringBuilder();
        builder.Append(message.Role).Append('\n');
        foreach (var block in message.ContentBlocks)
        {
            switch (block)
            {
                case ConversationTextBlock text:
                    builder.Append("text\u0000").Append(text.Text).Append('\u0001');
                    break;
                case ConversationToolUseBlock toolUse:
                    builder.Append("tool_use\u0000").Append(toolUse.ToolUseId).Append('\u0000')
                        .Append(toolUse.ToolName).Append('\u0000').Append(toolUse.InputJson).Append('\u0001');
                    break;
                case ConversationToolResultBlock toolResult:
                    builder.Append("tool_result\u0000").Append(toolResult.ToolUseId).Append('\u0000')
                        .Append(toolResult.IsError ? "1" : "0").Append('\u0000').Append(toolResult.Content).Append('\u0001');
                    break;
                default:
                    builder.Append(block.GetType().Name).Append('\u0001');
                    break;
            }
        }

        return Hash(builder.ToString());
    }

    public static RecordedSample Load(string path)
    {
        var json = File.ReadAllText(path);
        var sample = JsonSerializer.Deserialize<RecordedSample>(json, Options)
            ?? throw new InvalidDataException($"Recording at '{path}' deserialized to null.");

        if (sample.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Recording at '{path}' has schema_version {sample.SchemaVersion}; this build " +
                $"understands {CurrentSchemaVersion}. Re-capture the recording (never silently reinterpret).");
        }

        return sample;
    }

    public static void Save(string path, RecordedSample sample)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(sample, Options));
    }
}
