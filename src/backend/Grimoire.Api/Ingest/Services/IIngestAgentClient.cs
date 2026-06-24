namespace Grimoire.Api.Ingest.Services;

public interface IIngestAgentClient
{
    Task<(string RunId, string Status, string StartedAt)> TriggerRunAsync(string runId);
    Task<(string RunId, string Status, string? CompletedAt, int? TotalFiles)> GetRunStatusAsync(string runId);
    Task<object> SubmitFeedbackAsync(string runId, object feedbackResponse);
    Task<ConversationTurnResponse> SubmitConversationTurnAsync(string conversationId, string message);
}
