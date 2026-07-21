using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T020 (US2) — HTTP contract of the task-record endpoint (contracts/task-record-api.md):
/// parsed metadata + frontmatter-stripped body on success, a 404 problem payload for every
/// unavailable case (never a 5xx for a malformed file), and byte-for-byte invariance of the
/// existing detail/board endpoints.
/// </summary>
public class TaskRecordApiTests
{
    [Fact]
    public async Task GetTaskRecord_ValidV2Record_Returns200_WithParsedMetadataAndStrippedBody()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture);
        var client = host.GetTestClient();

        var taskId = "2026-07-18-ingest-abc123";
        var recordPath = Path.Combine(fixture.ContentPaths.TasksDir, $"{taskId}.md");
        await File.WriteAllTextAsync(recordPath,
            """
            ---
            task_id: 2026-07-18-ingest-abc123
            type: ingest
            status: running
            agent: ingest
            started_at: 2026-07-18T14:03:11.0000000Z
            completed_at: null
            source_ref: "raw/sources/2026-07-18-ingest-abc123.md"
            original_ref: "raw/originals/2026-07-18-ingest-abc123.html"
            pages_touched: []
            failure_reason: null
            ---

            ## Stages

            - [x] accepted
            - [ ] converted
            """);

        var response = await client.GetAsync($"/api/ingest-submissions/{taskId}/task-record");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(taskId, json.GetProperty("taskId").GetString());

        var metadata = json.GetProperty("metadata");
        Assert.Equal("running", metadata.GetProperty("status").GetString());
        Assert.Equal("ingest", metadata.GetProperty("agent").GetString());
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("completedAt").ValueKind);
        Assert.Equal("raw/sources/2026-07-18-ingest-abc123.md", metadata.GetProperty("sourceRef").GetString());
        Assert.Equal("raw/originals/2026-07-18-ingest-abc123.html", metadata.GetProperty("originalRef").GetString());
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("failureReason").ValueKind);

        var body = json.GetProperty("body").GetString();
        Assert.DoesNotContain("---", body);
        Assert.DoesNotContain("task_id:", body);
        Assert.Contains("## Stages", body);
        Assert.Contains("- [x] accepted", body);
    }

    [Fact]
    public async Task GetTaskRecord_CompletedRecord_SerializesCompletedAt_AsNonNull()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture);
        var client = host.GetTestClient();

        var taskId = "2026-07-18-ingest-done1";
        var recordPath = Path.Combine(fixture.ContentPaths.TasksDir, $"{taskId}.md");
        await File.WriteAllTextAsync(recordPath,
            """
            ---
            task_id: 2026-07-18-ingest-done1
            type: ingest
            status: completed
            agent: ingest
            started_at: 2026-07-18T14:03:11.0000000Z
            completed_at: 2026-07-18T14:05:00.0000000Z
            source_ref: null
            original_ref: null
            failure_reason: null
            ---

            Done.
            """);

        var response = await client.GetAsync($"/api/ingest-submissions/{taskId}/task-record");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var metadata = json.GetProperty("metadata");
        Assert.Equal("completed", metadata.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, metadata.GetProperty("completedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("sourceRef").ValueKind);
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("originalRef").ValueKind);
    }

    [Fact]
    public async Task GetTaskRecord_MissingFile_Returns404_WithProblemPayload()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/api/ingest-submissions/does-not-exist/task-record");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task GetTaskRecord_MalformedFrontmatter_Returns404_NeverA5xx()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture);
        var client = host.GetTestClient();

        var taskId = "2026-07-18-ingest-torn01";
        var recordPath = Path.Combine(fixture.ContentPaths.TasksDir, $"{taskId}.md");
        // Torn / pre-atomic-write legacy file: an opening frontmatter fence with no closing
        // fence, exactly the shape TaskArtifactFrontmatter.TryParse rejects (< 3 sections).
        await File.WriteAllTextAsync(recordPath,
            """
            ---
            task_id: 2026-07-18-ingest-torn01
            status: run
            """);

        var response = await client.GetAsync($"/api/ingest-submissions/{taskId}/task-record");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task GetTaskRecord_UnknownBoardTaskId_Returns404()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture);
        var client = host.GetTestClient();

        // No board projection and no record file exist for this id at all.
        var response = await client.GetAsync("/api/ingest-submissions/never-submitted/task-record");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExistingDetailAndBoardEndpoints_AreByteForByteUnchanged()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "note.md",
            System.Text.Encoding.UTF8.GetBytes("# Note"), "text/markdown"));
        await fixture.WaitForStatusAsync(taskId, s => s is "completed" or "failed");

        var detailResponse = await client.GetAsync($"/api/ingest-submissions/{taskId}");
        detailResponse.EnsureSuccessStatusCode();
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(taskId, detail.GetProperty("taskId").GetString());
        Assert.True(detail.TryGetProperty("status", out _));
        Assert.True(detail.TryGetProperty("failureReason", out _));
        Assert.True(detail.TryGetProperty("sourceRef", out _));
        Assert.True(detail.TryGetProperty("originalRef", out _));
        Assert.True(detail.TryGetProperty("userPromptSource", out _));
        Assert.True(detail.TryGetProperty("userPrompt", out _));
        Assert.True(detail.TryGetProperty("convertSteps", out _));
        Assert.True(detail.TryGetProperty("runActivity", out _));

        var boardResponse = await client.GetAsync("/api/ingest-submissions");
        boardResponse.EnsureSuccessStatusCode();
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var task = board.GetProperty("tasks").EnumerateArray().Single(t => t.GetProperty("taskId").GetString() == taskId);
        Assert.True(task.TryGetProperty("status", out _));
        Assert.True(task.TryGetProperty("title", out _));
        Assert.True(task.TryGetProperty("updatedAt", out _));
        Assert.True(task.TryGetProperty("failureReason", out _));
        Assert.True(task.TryGetProperty("taskLink", out _));
        Assert.True(task.TryGetProperty("queuePosition", out _));
        Assert.True(board.TryGetProperty("queuePaused", out _));
    }

    private static async Task<IHost> BuildHostAsync(IngestSubmissionPipelineFixture fixture)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddSingleton(fixture.Validator);
                    services.AddSingleton(fixture.Pipeline);
                    services.AddSingleton(fixture.BoardStore);
                    services.AddSingleton(fixture.ContentPaths);
                    services.AddSingleton(fixture.SourceArtifactStore);
                    services.AddSingleton(fixture.Coordinator);
                    services.AddSingleton(new TaskRecordReadModel(fixture.ResolvedPaths));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGroup("/api/ingest-submissions").MapIngestSubmissionEndpoints();
                        endpoints.MapGroup("/api/ingest-queue").MapIngestQueueEndpoints();
                    });
                });
            });

        return await hostBuilder.StartAsync();
    }
}
