using System.Net.Http.Json;
using System.Text.Json;

namespace Grimoire.Ingest.Hub;

public class HubClient : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HubClient> _logger;
    private readonly string? _hubUrl;
    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatTask;

    public bool IsConnected { get; private set; }

    public HubClient(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<HubClient> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _hubUrl = _configuration["IngestHubUrl"]
            ?? _configuration["INGEST_HUB_URL"];
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_hubUrl))
        {
            _logger.LogInformation("ingest.hub_unavailable hub_url=(none) — standalone mode");
            return;
        }

        await RegisterAsync(cancellationToken);

        _heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _heartbeatTask = RunHeartbeatAsync(_heartbeatCts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_heartbeatCts != null)
        {
            await _heartbeatCts.CancelAsync();
        }

        if (_heartbeatTask != null)
        {
            try { await _heartbeatTask; }
            catch (OperationCanceledException) { /* expected */ }
        }
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        var payload = new
        {
            agentId = "ingest",
            name = "Ingest Agent",
            capabilities = new[] { "file-ingest", "llm-analysis", "git-commit" }
        };

        try
        {
            var client = _httpClientFactory.CreateClient("hub");
            var response = await client.PostAsJsonAsync($"{_hubUrl}/api/agents", payload, cancellationToken);
            response.EnsureSuccessStatusCode();
            IsConnected = true;
            _logger.LogInformation(
                "ingest.hub_registered hub_url={HubUrl} agent_id=ingest",
                _hubUrl);
        }
        catch (Exception ex)
        {
            IsConnected = false;
            _logger.LogWarning(
                "ingest.hub_unavailable hub_url={HubUrl} error={Error}",
                _hubUrl, ex.Message);
        }
    }

    private async Task RunHeartbeatAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);

            try
            {
                var client = _httpClientFactory.CreateClient("hub");
                var response = await client.GetAsync($"{_hubUrl}/health", ct);
                IsConnected = response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                _logger.LogWarning(
                    "ingest.hub_unavailable hub_url={HubUrl} error={Error}",
                    _hubUrl, ex.Message);
            }
        }
    }
}
