using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Grimoire.Api.Tests.Fixtures;

namespace Grimoire.Api.Tests.Integration.Ingest;

/// <summary>
/// Integration tests for Ingest channel endpoints: upload, trigger, feedback, conversation.
/// Uses in-memory SQLite and mock agent for isolation.
/// </summary>
public class IngestEndpointsTests
{
    private readonly HttpClient _client;

    public IngestEndpointsTests()
    {
        var factory = new ApiWebApplicationFactory();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UploadSource_WithValidFiles_Returns202Accepted()
    {
        var content = new MultipartFormDataContent();
        var fileContent = new StringContent("test file content");
        content.Add(fileContent, "files", "test.md");
        content.Add(new StringContent("docs"), "subDirectory");

        var response = await _client.PostAsync("/api/ingest/upload", content);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("accepted", out _));
        Assert.True(doc.RootElement.TryGetProperty("rejected", out _));
    }

    [Fact]
    public async Task TriggerIngest_WithNoActiveRun_Returns202Accepted()
    {
        var response = await _client.PostAsJsonAsync("/api/ingest/trigger", new { });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("runId", out var runId));
        Assert.False(string.IsNullOrWhiteSpace(runId.GetString()));
    }

    [Fact]
    public async Task TriggerIngest_WithActiveRun_Returns409Conflict()
    {
        await _client.PostAsJsonAsync("/api/ingest/trigger", new { });

        var response = await _client.PostAsJsonAsync("/api/ingest/trigger", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_WithValidRequest_Returns200Ok()
    {
        var feedback = new
        {
            requestId = Guid.NewGuid().ToString(),
            filePath = "test.pdf",
            action = "Process",
            tagValue = (string?)null
        };

        var response = await _client.PostAsJsonAsync("/api/ingest/runs/test-run/feedback", feedback);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetIngestRun_WithValidRunId_ReturnsRunDetails()
    {
        var trigger = await _client.PostAsJsonAsync("/api/ingest/trigger", new { });
        var triggerJson = await trigger.Content.ReadAsStringAsync();
        using var triggerDoc = JsonDocument.Parse(triggerJson);
        var runId = triggerDoc.RootElement.GetProperty("runId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(runId));

        var response = await _client.GetAsync($"/api/ingest/runs/{runId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
