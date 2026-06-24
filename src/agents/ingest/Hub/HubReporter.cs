using System.Net.Http.Json;
using Grimoire.Ingest.Models;

namespace Grimoire.Ingest.Hub;

public class HubReporter
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HubReporter> _logger;
    private readonly string? _hubUrl;

    public HubReporter(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<HubReporter> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _hubUrl = _configuration["IngestHubUrl"];
    }

    public async Task PostProgressAsync(IngestProgressPayload payload)
    {
        await PostAsync("/api/ingest/callbacks/progress", payload);
    }

    public async Task PostFeedbackRequestAsync(IngestFeedbackPayload payload)
    {
        await PostAsync("/api/ingest/callbacks/feedback-request", payload);
    }

    public async Task PostConversationOpenedAsync(ConversationOpenedPayload payload)
    {
        await PostAsync("/api/ingest/callbacks/conversation-opened", payload);
    }

    public async Task PostRunCompletedAsync(RunCompletedPayload payload)
    {
        await PostAsync("/api/ingest/callbacks/run-completed", payload);
    }

    private async Task PostAsync<T>(string path, T payload)
    {
        if (string.IsNullOrWhiteSpace(_hubUrl))
            return;

        try
        {
            var client = _httpClientFactory.CreateClient("hub");
            var response = await client.PostAsJsonAsync($"{_hubUrl}{path}", payload);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "ingest.hub_unavailable hub_url={HubUrl} path={Path} status={Status}",
                    _hubUrl, path, (int)response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                "ingest.hub_unavailable hub_url={HubUrl} path={Path} error={Error}",
                _hubUrl, path, ex.Message);
        }
    }
}

public record IngestProgressPayload(
    string RunId,
    string FilePath,
    string Status,
    int ChunkCount,
    int DurationMs,
    int ProcessedSoFar,
    int TotalFiles,
    string? ErrorMessage = null);

public record IngestFeedbackPayload(
    string RequestId,
    string RunId,
    string FilePath,
    string Reason,
    object[] Options);

public record ConversationOpenedPayload(
    string ConversationId,
    string RunId,
    string FilePath,
    string OpeningMessage,
    DateTimeOffset CreatedAt);

public record RunCompletedPayload(
    string RunId,
    string Status,
    DateTimeOffset CompletedAt,
    object Summary);
