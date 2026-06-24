using Grimoire.Api.Ingest.Services;

namespace Grimoire.Api.Tests.Stubs;

/// <summary>
/// In-memory stub implementation of IIngestAgentClient for integration tests.
/// Returns predictable responses without requiring a running ingest agent.
/// </summary>
public class StubIngestAgentClient : IIngestAgentClient
{
    public Task<(string RunId, string Status, string StartedAt)> TriggerRunAsync(string runId)
    {
        return Task.FromResult((runId, "Running", DateTime.UtcNow.ToString("O")));
    }

    public Task<(string RunId, string Status, string? CompletedAt, int? TotalFiles)> GetRunStatusAsync(string runId)
    {
        return Task.FromResult<(string, string, string?, int?)>((runId, "Running", null, null));
    }

    public Task<object> SubmitFeedbackAsync(string runId, object feedbackResponse)
    {
        return Task.FromResult<object>(new { });
    }

    public Task<ConversationTurnResponse> SubmitConversationTurnAsync(string conversationId, string message)
    {
        return Task.FromResult(new ConversationTurnResponse(
            ConversationId: conversationId,
            TurnIndex: 0,
            Role: "agent",
            Message: "stub response",
            CreatedAt: DateTime.UtcNow.ToString("O")));
    }
}
