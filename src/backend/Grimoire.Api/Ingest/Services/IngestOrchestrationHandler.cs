using Grimoire.Api.Ingest.Hubs;
using Grimoire.Api.Ingest.Models;
using Grimoire.Api.Ingest.Models.SignalREvents;
using Grimoire.Api.Ingest.Persistence;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace Grimoire.Api.Ingest.Services;

public class IngestOrchestrationHandler
{
    private readonly IIngestAgentClient _agentClient;
    private readonly IngestRepository _repository;
    private readonly IHubContext<IngestHub> _hubContext;
    private readonly ILogger<IngestOrchestrationHandler> _logger;

    public IngestOrchestrationHandler(
        IIngestAgentClient agentClient,
        IngestRepository repository,
        IHubContext<IngestHub> hubContext,
        ILogger<IngestOrchestrationHandler> logger)
    {
        _agentClient = agentClient;
        _repository = repository;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<(string RunId, string Status, string StartedAt)> TriggerRunAsync(string? requestedRunId = null)
    {
        var runId = requestedRunId ?? Guid.NewGuid().ToString();

        try
        {
            var (returnedRunId, status, startedAt) = await _agentClient.TriggerRunAsync(runId);

            var record = new IngestRunRecord
            {
                RunId = returnedRunId,
                Status = status,
                StartedAt = startedAt,
                CompletedAt = null
            };
            await _repository.SaveIngestRunAsync(record);

            var payload = new IngestRunStarted(returnedRunId, startedAt, 0);
            await _hubContext.Clients.All.SendAsync("IngestRunStarted", payload);

            _logger.LogInformation("ingest_run_triggered runId={RunId} status={Status}", returnedRunId, status);
            return (returnedRunId, status, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ingest_trigger_run_failed runId={RunId}", runId);
            throw;
        }
    }

    public async Task<object> SubmitFeedbackAsync(string runId, object feedbackResponse)
    {
        try
        {
            var result = await _agentClient.SubmitFeedbackAsync(runId, feedbackResponse);
            _logger.LogInformation("ingest_feedback_submitted runId={RunId}", runId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ingest_submit_feedback_failed runId={RunId}", runId);
            throw;
        }
    }

    public async Task<ConversationTurnResponse> SubmitConversationTurnAsync(string conversationId, string message)
    {
        try
        {
            var result = await _agentClient.SubmitConversationTurnAsync(conversationId, message);

            var existingTurns = await _repository.GetConversationTurnsAsync(conversationId);
            var filePath = existingTurns.FirstOrDefault()?.FilePath ?? string.Empty;

            await _repository.SaveConversationTurnAsync(new ConversationTurnRecord(
                ConversationId: result.ConversationId,
                TurnIndex: result.TurnIndex,
                FilePath: filePath,
                Role: result.Role,
                Message: result.Message,
                CreatedAt: result.CreatedAt));

            await _hubContext.Clients.All.SendAsync("IngestConversationTurn", new IngestConversationTurn(
                ConversationId: result.ConversationId,
                TurnIndex: result.TurnIndex,
                Role: result.Role,
                Message: result.Message,
                CreatedAt: result.CreatedAt));

            _logger.LogInformation("ingest_conversation_turn_submitted conversationId={ConversationId}", conversationId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ingest_submit_conversation_turn_failed conversationId={ConversationId}", conversationId);
            throw;
        }
    }

    public async Task HandleProgressCallbackAsync(string runId, IngestProgress progress)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("IngestProgress", progress);
            _logger.LogInformation("ingest_progress_broadcasted runId={RunId} filePath={FilePath} status={Status}",
                runId, progress.FilePath, progress.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ingest_handle_progress_failed runId={RunId}", runId);
        }
    }

    public async Task HandleFeedbackRequestCallbackAsync(string runId, IngestFeedbackRequest feedbackRequest)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("IngestFeedbackRequest", feedbackRequest);
            _logger.LogInformation("ingest_feedback_request_broadcasted runId={RunId} requestId={RequestId}",
                runId, feedbackRequest.RequestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ingest_handle_feedback_request_failed runId={RunId}", runId);
        }
    }

    public async Task HandleConversationOpenedCallbackAsync(string conversationId, IngestConversationOpened payload)
    {
        try
        {
            await _repository.SaveConversationTurnAsync(new ConversationTurnRecord(
                ConversationId: conversationId,
                TurnIndex: 0,
                FilePath: payload.FilePath,
                Role: "agent",
                Message: payload.OpeningMessage,
                CreatedAt: payload.CreatedAt));

            await _hubContext.Clients.All.SendAsync("IngestConversationOpened", payload);
            _logger.LogInformation("ingest_conversation_opened_broadcasted conversationId={ConversationId}",
                conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ingest_handle_conversation_opened_failed conversationId={ConversationId}", conversationId);
        }
    }

    public async Task HandleConversationTurnCallbackAsync(string conversationId, IngestConversationTurn turn)
    {
        try
        {
            var existingTurns = await _repository.GetConversationTurnsAsync(conversationId);
            var filePath = existingTurns.FirstOrDefault()?.FilePath ?? string.Empty;

            var record = new ConversationTurnRecord(
                ConversationId: conversationId,
                TurnIndex: turn.TurnIndex,
                FilePath: filePath,
                Role: turn.Role,
                Message: turn.Message,
                CreatedAt: turn.CreatedAt
            );
            await _repository.SaveConversationTurnAsync(record);
            await _hubContext.Clients.All.SendAsync("IngestConversationTurn", turn);
            _logger.LogInformation("ingest_conversation_turn_broadcasted conversationId={ConversationId} turnIndex={TurnIndex}",
                conversationId, turn.TurnIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ingest_handle_conversation_turn_failed conversationId={ConversationId}", conversationId);
        }
    }

    public async Task HandleRunCompletedCallbackAsync(string runId, IngestRunCompleted completed)
    {
        try
        {
            var existing = await _repository.GetIngestRunAsync(runId);
            var summary = TryParseSummary(completed.Summary);

            var record = new IngestRunRecord
            {
                RunId = runId,
                Status = completed.Status,
                StartedAt = existing?.StartedAt ?? completed.CompletedAt,
                CompletedAt = completed.CompletedAt,
                TotalFiles = summary.TotalFiles,
                ProcessedFiles = summary.ProcessedCount,
                FailedFiles = summary.FailedCount,
                SkippedFiles = summary.SkippedCount,
                DurationMs = summary.DurationMs,
                FileResults = summary.FileResults
            };
            await _repository.SaveIngestRunAsync(record);
            await _hubContext.Clients.All.SendAsync("IngestRunCompleted", completed);
            _logger.LogInformation("ingest_run_completed_broadcasted runId={RunId} status={Status}",
                runId, completed.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ingest_handle_run_completed_failed runId={RunId}", runId);
        }
    }

    public async Task HandleLogEntryCallbackAsync(string runId, IngestLogEntry logEntry)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("IngestLogEntry", logEntry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ingest_handle_log_entry_failed runId={RunId}", runId);
        }
    }

    private static ParsedSummary TryParseSummary(object summary)
    {
        if (summary is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return ParsedSummary.Empty;
        }

        var results = new List<FileProcessingResult>();
        if (json.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in files.EnumerateArray())
            {
                results.Add(new FileProcessingResult
                {
                    FilePath = item.TryGetProperty("filePath", out var filePath) ? filePath.GetString() ?? string.Empty : string.Empty,
                    Status = item.TryGetProperty("status", out var status) ? status.GetString() ?? "unknown" : "unknown",
                    ChunkCount = item.TryGetProperty("chunkCount", out var chunkCount) ? chunkCount.GetInt32() : 0,
                    DurationMs = item.TryGetProperty("durationMs", out var durationMs) ? durationMs.GetInt64() : 0,
                    ErrorMessage = item.TryGetProperty("errorMessage", out var errorMessage)
                        && errorMessage.ValueKind != JsonValueKind.Null
                        ? errorMessage.GetString()
                        : null
                });
            }
        }

        return new ParsedSummary(
            TotalFiles: json.TryGetProperty("totalFiles", out var totalFiles) ? totalFiles.GetInt32() : 0,
            ProcessedCount: json.TryGetProperty("processedCount", out var processedCount) ? processedCount.GetInt32() : 0,
            FailedCount: json.TryGetProperty("failedCount", out var failedCount) ? failedCount.GetInt32() : 0,
            SkippedCount: json.TryGetProperty("skippedCount", out var skippedCount) ? skippedCount.GetInt32() : 0,
            DurationMs: json.TryGetProperty("durationMs", out var durationMsValue) ? durationMsValue.GetInt64() : 0,
            FileResults: results);
    }

    private readonly record struct ParsedSummary(
        int TotalFiles,
        int ProcessedCount,
        int FailedCount,
        int SkippedCount,
        long DurationMs,
        List<FileProcessingResult> FileResults)
    {
        public static ParsedSummary Empty { get; } = new(0, 0, 0, 0, 0, new List<FileProcessingResult>());
    }
}
