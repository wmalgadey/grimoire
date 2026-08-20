using System.Net;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.IntegrationTests.TestSupport;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #123 — the adapter's HTTP logging handler used to write the complete body of every
/// request and every response at <c>Information</c>. At default levels that duplicated the
/// system prompt, the whole conversation, every ingested source document, and every page
/// body the agent wrote into the process log on every turn; since the conversation is
/// re-sent in full each turn, the volume grew quadratically over a run of up to 50 turns.
/// Ingested sources are untrusted external documents and may be private.
/// <para>
/// What is asserted is what Grimoire itself decides — which level each line is written at,
/// and whether the body is read at all — through the real handler doing real HTTP against
/// <see cref="FakeAnthropicEndpoint"/>.
/// </para>
/// </summary>
public class ModelClientRequestLoggingTests
{
    [Fact]
    public async Task AtInformation_TheTransactionIsLogged_AndTheBodiesAreNot()
    {
        const string secretInTheConversation = "a-private-sentence-from-an-ingested-source";
        var logger = new RecordingLogger(LogLevel.Information);

        await NextTurnAsync(logger, secretInTheConversation);

        Assert.Contains(logger.Entries, e => e.Message.Contains("Anthropic request: POST", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, e => e.Message.Contains("Anthropic response: OK", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains(secretInTheConversation, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("request body", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("response body", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AtDebug_TheBodiesAreAvailable_ButOnlyAtDebug()
    {
        // Dropping the level must not delete the diagnostic — someone chasing a provider
        // rejection still needs the payload, they just have to ask for it.
        const string marker = "a-private-sentence-from-an-ingested-source";
        var logger = new RecordingLogger(LogLevel.Debug);

        await NextTurnAsync(logger, marker);

        var bodyEntry = Assert.Single(
            logger.Entries.Where(e => e.Message.Contains("Anthropic request body", StringComparison.Ordinal)));
        Assert.Equal(LogLevel.Debug, bodyEntry.Level);
        Assert.Contains(marker, bodyEntry.Message, StringComparison.Ordinal);

        Assert.All(
            logger.Entries.Where(e => e.Message.Contains("body", StringComparison.OrdinalIgnoreCase)),
            e => Assert.Equal(LogLevel.Debug, e.Level));
    }

    [Fact]
    public async Task AtDebug_AStreamedResponseBodyIsStillNotBuffered()
    {
        // The other cost this issue names is that both overloads buffered the whole body
        // before forwarding it. On Query's streamed turn that is not merely wasteful:
        // reading an event-stream to a string waits for the stream to finish, which is
        // exactly the delay streaming exists to avoid (ADR-011 SC-003). Turning on a log
        // level must not change how the product behaves, so the streamed body is never
        // read — the request body still is, and the deltas still arrive.
        var logger = new RecordingLogger(LogLevel.Debug);

        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.StreamingMessageBody("Streamed answer."),
            FakeAnthropicEndpoint.StreamingContentType);

        var deltas = new List<string>();
        var turn = await NextTurnAgainstAsync(provider, logger, "a-streamed-question", deltas.Add);

        Assert.Equal("Streamed answer.", turn.AssistantText);
        Assert.Equal(["Streamed answer."], deltas);

        Assert.Contains(logger.Entries, e => e.Message.Contains("Anthropic request body", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("Anthropic response body", StringComparison.Ordinal));
    }

    private static async Task NextTurnAsync(ILogger<AnthropicModelClient> logger, string conversationText)
    {
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK, FakeAnthropicEndpoint.MessageBody("end_turn", text: "Done."));

        await NextTurnAgainstAsync(provider, logger, conversationText);
    }

    private static Task<ModelTurn> NextTurnAgainstAsync(
        FakeAnthropicEndpoint provider,
        ILogger<AnthropicModelClient> logger,
        string conversationText,
        Action<string>? onTextDelta = null)
    {
        using var scope = ModelClientEnvironmentScope.PointingAt(provider.BaseUrl);
        var client = new AnthropicModelClient(
            logger,
            modelEnvVar: scope.ModelEnvVar,
            baseUrlEnvVar: scope.BaseUrlEnvVar,
            maxOutputTokensEnvVar: $"GRIMOIRE_TEST_MAX_OUTPUT_UNSET_{Guid.NewGuid():N}");

        return client.NextTurnAsync(
            "You are a test agent.",
            [new ConversationMessage("user", conversationText)],
            ToolRegistry.Default.Tools,
            CancellationToken.None,
            onTextDelta);
    }

    /// <summary>
    /// A hand-rolled <see cref="ILogger{TCategoryName}"/> that keeps what was written, so
    /// the assertions are state-based (Principle II) — what ended up in the log, not which
    /// calls were made on a mock.
    /// </summary>
    private sealed class RecordingLogger(LogLevel minimum) : ILogger<AnthropicModelClient>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
