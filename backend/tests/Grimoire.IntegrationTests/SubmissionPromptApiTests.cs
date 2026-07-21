using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Grimoire.Hub.IngestSubmission;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T016 (US2/US3) — HTTP contract of the 004 extension
/// (contracts/ingest-submission-api-extension.md): defaults endpoint as single source
/// of truth (fail-closed 500), prompt length rejection before task creation, and the
/// extended acceptance payload.
/// </summary>
public class SubmissionPromptApiTests
{
    [Fact]
    public async Task GetDefaults_ReturnsVerbatimDefaultPrompt_MaxLength_AndStepRegistry()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/api/ingest-submissions/defaults");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Please integrate the source.", json.GetProperty("defaultUserPrompt").GetString());
        Assert.Equal(8000, json.GetProperty("userPromptMaxLength").GetInt32());

        var step = Assert.Single(json.GetProperty("convertSteps").EnumerateArray());
        Assert.Equal("markitdown", step.GetProperty("name").GetString());
        Assert.True(step.GetProperty("defaultEnabled").GetBoolean());
        Assert.Contains("pdf_file", step.GetProperty("requiredFor").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("url", step.GetProperty("appliesTo").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task GetDefaults_FailsClosed_WhenDefaultPromptDocumentIsMissing()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        File.Delete(fixture.ContentPaths.DefaultUserPromptPath);

        using var host = await BuildHostAsync(fixture);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/api/ingest-submissions/defaults");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("default-user-prompt.md", json.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedUserPrompt_IsRejectedWith400_BeforeAnyTaskIsCreated()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture);
        var client = host.GetTestClient();

        var oversized = new string('x', IngestSubmissionValidator.UserPromptMaxLength + 1);
        var response = await client.PostAsJsonAsync("/api/ingest-submissions",
            new { kind = "url", url = "https://example.test/a", userPrompt = oversized });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("user_prompt_too_long", json.GetProperty("message").GetString(), StringComparison.Ordinal);

        // FR-010: rejected before task creation — no artifact, no board entry.
        Assert.Empty(Directory.GetFiles(fixture.ContentPaths.TasksDir, "*.md"));
    }

    [Fact]
    public async Task RequiredStepDisabled_ViaHttp_IsRejectedWith422_BeforeAnyTaskIsCreated()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture);
        var client = host.GetTestClient();

        using var form = new MultipartFormDataContent
        {
            { new StringContent("pdf_file"), "kind" },
            { new StringContent("{\"markitdown\": false}"), "convertSteps" },
            { new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "doc.pdf" },
        };

        var response = await client.PostAsync("/api/ingest-submissions", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("convert_step_required", json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(fixture.ContentPaths.TasksDir, "*.md"));
    }

    [Fact]
    public async Task AcceptedSubmission_EchoesPromptSourceAndEffectiveConvertSteps()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/ingest-submissions",
            new { kind = "url", url = "https://example.test/a", userPrompt = "Steer this run.", convertSteps = new { markitdown = false } });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("custom", json.GetProperty("userPromptSource").GetString());
        Assert.False(json.GetProperty("convertSteps").GetProperty("markitdown").GetBoolean());
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
