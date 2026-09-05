using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T023 (023-task-ui-improvements, US4 / FR-001, FR-002, SC-001, SC-002): the task detail's
/// `source` object and the read-only source-content endpoint. URL tasks link directly; file
/// tasks link to a Hub-served stream of the persisted original; a missing manifest or original
/// answers with `available:false` — never a broken link. Contract: contracts/http-api.md.
/// </summary>
public class IngestSourceContentApiTests
{
    [Fact]
    public async Task Detail_ForFileSubmission_ExposesSourceKindFile_WithTheServeEndpointHref()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "note.md",
            Encoding.UTF8.GetBytes("# Note\n\nBody.\n"), "text/markdown"));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        var source = await SourceObjectAsync(client, taskId);
        Assert.Equal("file", source.GetProperty("kind").GetString());
        Assert.True(source.GetProperty("available").GetBoolean());
        Assert.Equal($"/api/ingest-submissions/{taskId}/source/original", source.GetProperty("href").GetString());
    }

    [Fact]
    public async Task Detail_ForUrlSubmission_ExposesSourceKindUrl_WithTheSubmittedUrlAsHref()
    {
        using var fixture = new IngestSubmissionPipelineFixture(
            urlFetchHandler: new StaticHtmlHandler());
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.Url, "https://example.test/article", null, null, null));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        var source = await SourceObjectAsync(client, taskId);
        Assert.Equal("url", source.GetProperty("kind").GetString());
        Assert.True(source.GetProperty("available").GetBoolean());
        Assert.Equal("https://example.test/article", source.GetProperty("href").GetString());
    }

    [Fact]
    public async Task Detail_WhenTheManifestIsMissing_ReportsSourceUnavailable_WithNullHref()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = "2026-08-13-ingest-nomanifest-src";
        await IngestTaskDetailHistoryTests.WriteTaskArtifactAsync(fixture, taskId, "failed", failureReason: "Conversion failed.");

        var source = await SourceObjectAsync(client, taskId);
        Assert.False(source.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, source.GetProperty("href").ValueKind);
    }

    [Fact]
    public async Task Detail_WhenTheOriginalFileWasDeleted_ReportsSourceUnavailable()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "gone.md",
            Encoding.UTF8.GetBytes("# Gone\n\nBody.\n"), "text/markdown"));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        var manifest = await fixture.IngestSourceArtifactStore.TryReadMetadataAsync(taskId);
        Assert.NotNull(manifest);
        File.Delete(manifest!.OriginalPath);

        var source = await SourceObjectAsync(client, taskId);
        Assert.False(source.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, source.GetProperty("href").ValueKind);
    }

    [Fact]
    public async Task SourceOriginal_StreamsTheStoredOriginal_WithManifestContentTypeAndInlineDisposition()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var originalBytes = Encoding.UTF8.GetBytes("# Streamed\n\nThe exact original bytes.\n");
        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "streamed.md", originalBytes, "text/markdown"));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        var response = await client.GetAsync($"/api/ingest-submissions/{taskId}/source/original");
        response.EnsureSuccessStatusCode();

        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(originalBytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task SourceOriginal_UnknownTask_Returns404()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/api/ingest-submissions/no-such-task/source/original");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SourceOriginal_MissingFile_Returns404()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "vanish.md",
            Encoding.UTF8.GetBytes("# Vanish\n\nBody.\n"), "text/markdown"));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        var manifest = await fixture.IngestSourceArtifactStore.TryReadMetadataAsync(taskId);
        File.Delete(manifest!.OriginalPath);

        var response = await client.GetAsync($"/api/ingest-submissions/{taskId}/source/original");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// FR-002/SC-002 by construction: the path is derived exclusively from the route
    /// <c>taskId</c> — the endpoint accepts no other path input, so there is nothing a
    /// caller could smuggle in to escape <c>RawOriginalsDir</c>.
    /// </summary>
    [Fact]
    public async Task SourceOriginal_TaskIdContainingPathSegments_Returns404_NeverEscapesTheOriginalsDir()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var response = await client.GetAsync(
            $"/api/ingest-submissions/{Uri.EscapeDataString("../../etc/passwd")}/source/original");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<JsonElement> SourceObjectAsync(HttpClient client, string taskId)
    {
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/ingest-submissions/{taskId}");
        return detail.GetProperty("source");
    }

    private sealed class StaticHtmlHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body><p>Article</p></body></html>", Encoding.UTF8, "text/html"),
            });
    }
}
