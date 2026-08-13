using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T019 (023-task-ui-improvements, US3 / FR-003, SC-003): every task carries a human-readable
/// label on the board and in the detail view. The title is extracted deterministically from
/// the normalized markdown's first ATX heading — display metadata, not wiki-content judgment
/// (Principle V) — with a fallback chain for sources that carry no heading.
/// </summary>
public class IngestTaskTitleTests
{
    [Fact]
    public async Task FileSubmission_WithLeadingH1_UsesThatHeadingAsTheTitle_OnBoardAndDetail()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await SubmitMarkdownAsync(fixture, "getting-started.md", "# Getting Started\n\nBody.\n");
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        Assert.Equal("Getting Started", await BoardTitleAsync(client, taskId));
        Assert.Equal("Getting Started", await DetailTitleAsync(client, taskId));
    }

    [Fact]
    public async Task FileSubmission_WithoutHeading_FallsBackToTheUploadedFilename()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await SubmitMarkdownAsync(fixture, "release-notes.md", "Just a paragraph, no heading.\n");
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        Assert.Equal("release-notes.md", await BoardTitleAsync(client, taskId));
        Assert.Equal("release-notes.md", await DetailTitleAsync(client, taskId));
    }

    [Fact]
    public async Task UrlSubmission_WithoutHeading_FallsBackToTheSubmittedUrl()
    {
        using var fixture = new IngestSubmissionPipelineFixture(
            urlFetchHandler: new StaticContentHandler("Plain text with no heading.", "text/plain"));
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.Url, "https://example.test/article", null, null, null,
            ConvertSteps: new Dictionary<string, bool> { ["markitdown"] = false }));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        Assert.Equal("https://example.test/article", await BoardTitleAsync(client, taskId));
        Assert.Equal("https://example.test/article", await DetailTitleAsync(client, taskId));
    }

    [Fact]
    public async Task UrlSubmission_WithLeadingH1_PrefersTheExtractedTitleOverTheUrl()
    {
        using var fixture = new IngestSubmissionPipelineFixture(
            urlFetchHandler: new StaticContentHandler("# Page Title\n\nBody.", "text/plain"));
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.Url, "https://example.test/article", null, null, null,
            ConvertSteps: new Dictionary<string, bool> { ["markitdown"] = false }));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        Assert.Equal("Page Title", await DetailTitleAsync(client, taskId));
    }

    /// <summary>
    /// A task whose conversion failed before the manifest was written has no title metadata
    /// at all — the chain still has to produce something identifying rather than an empty
    /// label (FR-003), and the task id is the only thing left.
    /// </summary>
    [Fact]
    public async Task TaskWithoutAManifest_FallsBackToTheTaskId()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = "2026-08-13-ingest-nomanifest";
        await IngestTaskDetailHistoryTests.WriteTaskArtifactAsync(fixture, taskId, "failed", failureReason: "Conversion failed.");

        Assert.Equal(taskId, await BoardTitleAsync(client, taskId));
        Assert.Equal(taskId, await DetailTitleAsync(client, taskId));
    }

    [Fact]
    public async Task OverlongHeading_IsCappedAt120Characters()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var heading = new string('a', 200);
        var taskId = await SubmitMarkdownAsync(fixture, "long.md", $"# {heading}\n\nBody.\n");
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        var title = await DetailTitleAsync(client, taskId);
        Assert.Equal(120, title!.Length);
        Assert.Equal(new string('a', 120), title);
    }

    [Fact]
    public async Task HeadingExtraction_IgnoresDeeperHeadings_AndTrimsSurroundingWhitespace()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await SubmitMarkdownAsync(
            fixture, "sections.md", "## Not the title\n\n#    Real Title   \n\nBody.\n");
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        Assert.Equal("Real Title", await DetailTitleAsync(client, taskId));
    }

    private static Task<string> SubmitMarkdownAsync(
        IngestSubmissionPipelineFixture fixture, string fileName, string markdown) =>
        fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, fileName, Encoding.UTF8.GetBytes(markdown), "text/markdown"));

    private static async Task<string?> BoardTitleAsync(HttpClient client, string taskId)
    {
        var board = await client.GetFromJsonAsync<JsonElement>("/api/ingest-submissions");
        return board.GetProperty("tasks").EnumerateArray()
            .Single(t => t.GetProperty("taskId").GetString() == taskId)
            .GetProperty("title").GetString();
    }

    private static async Task<string?> DetailTitleAsync(HttpClient client, string taskId)
    {
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/ingest-submissions/{taskId}");
        return detail.GetProperty("title").GetString();
    }

    private sealed class StaticContentHandler(string content, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, contentType),
            });
    }
}
