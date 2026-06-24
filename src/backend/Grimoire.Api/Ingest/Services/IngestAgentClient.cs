using System.Text.Json;

namespace Grimoire.Api.Ingest.Services;

public class IngestAgentClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IngestAgentClient> _logger;

    public IngestAgentClient(HttpClient httpClient, ILogger<IngestAgentClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<(string RunId, string Status, string StartedAt)> TriggerRunAsync(string runId)
    {
        var payload = new { runId };
        var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync("/ingest/runs", content);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("agent_trigger_run_failed runId={RunId} statusCode={StatusCode}", runId, response.StatusCode);
                response.EnsureSuccessStatusCode();
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return (
                root.GetProperty("runId").GetString() ?? runId,
                root.GetProperty("status").GetString() ?? "Running",
                root.GetProperty("startedAt").GetString() ?? DateTime.UtcNow.ToString("O")
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "agent_trigger_run_exception runId={RunId}", runId);
            throw;
        }
    }

    public async Task<(string RunId, string Status, string? CompletedAt, int? TotalFiles)> GetRunStatusAsync(string runId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/ingest/runs/{runId}");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("agent_get_run_status_failed runId={RunId} statusCode={StatusCode}", runId, response.StatusCode);
                return (runId, "Unknown", null, null);
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return (
                root.GetProperty("runId").GetString() ?? runId,
                root.GetProperty("status").GetString() ?? "Unknown",
                root.TryGetProperty("completedAt", out var completedAt) && completedAt.ValueKind != JsonValueKind.Null
                    ? completedAt.GetString()
                    : null,
                root.TryGetProperty("totalFiles", out var totalFiles) && totalFiles.ValueKind != JsonValueKind.Null
                    ? totalFiles.GetInt32()
                    : null
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "agent_get_run_status_exception runId={RunId}", runId);
            return (runId, "Unknown", null, null);
        }
    }

    public async Task<object> SubmitFeedbackAsync(string runId, object feedbackResponse)
    {
        var content = new StringContent(JsonSerializer.Serialize(feedbackResponse), System.Text.Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync($"/ingest/runs/{runId}/feedback", content);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("agent_submit_feedback_failed runId={RunId} statusCode={StatusCode}", runId, response.StatusCode);
                response.EnsureSuccessStatusCode();
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<object>(json) ?? new { };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "agent_submit_feedback_exception runId={RunId}", runId);
            throw;
        }
    }

    public async Task<object> SubmitConversationTurnAsync(string conversationId, string message)
    {
        var payload = new { message };
        var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync($"/ingest/conversations/{conversationId}/turns", content);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("agent_submit_conversation_turn_failed conversationId={ConversationId} statusCode={StatusCode}", conversationId, response.StatusCode);
                response.EnsureSuccessStatusCode();
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<object>(json) ?? new { };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "agent_submit_conversation_turn_exception conversationId={ConversationId}", conversationId);
            throw;
        }
    }
}
