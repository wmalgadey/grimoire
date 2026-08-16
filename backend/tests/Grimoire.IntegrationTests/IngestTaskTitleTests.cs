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

    // ---------------------------------------------------------------------------------
    // T044 (023-task-ui-improvements, FR-003 + converge input): the same label the detail
    // endpoint serves is also written into the task artifact's frontmatter, so the file on
    // disk and the UI cannot disagree about what a task is called. The manifest stays the
    // source of truth (ADR-025 driver); the frontmatter field is a mirrored copy, written
    // by the Hub and carried across the agent's own writes (ADR-002 artifact ownership).
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TaskArtifact_AtAHubWrittenStage_CarriesTheSameTitleTheDetailEndpointServes()
    {
        // autoPlay: false — the agent never takes the artifact over, so the file under
        // assertion is exactly what HubTaskArtifactWriter wrote for the `queued` stage.
        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: new FakeAgentProcessLauncher(autoPlay: false));
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await SubmitMarkdownAsync(fixture, "getting-started.md", "# Getting Started\n\nBody.\n");
        await fixture.WaitForStatusAsync(taskId, s => s == "queued");

        Assert.Equal("Getting Started", await ArtifactTitleAsync(fixture, taskId));
        Assert.Equal("Getting Started", await DetailTitleAsync(client, taskId));
    }

    [Fact]
    public async Task TaskArtifact_AfterTheAgentsOwnWrite_StillCarriesTheTitle()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await SubmitMarkdownAsync(fixture, "getting-started.md", "# Getting Started\n\nBody.\n");
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        // The agent process owns the artifact from `running` on (ADR-002). The title must
        // survive that handover rather than being dropped when the agent rewrites the file.
        Assert.Equal("completed", await ArtifactStatusAsync(fixture, taskId));
        Assert.Equal("Getting Started", await ArtifactTitleAsync(fixture, taskId));
        Assert.Equal("Getting Started", await DetailTitleAsync(client, taskId));
    }

    [Fact]
    public async Task TaskArtifact_WithoutAHeading_CarriesTheUploadedFilenameFallback()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await SubmitMarkdownAsync(fixture, "release-notes.md", "Just a paragraph, no heading.\n");
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        Assert.Equal("release-notes.md", await ArtifactTitleAsync(fixture, taskId));
        Assert.Equal("release-notes.md", await DetailTitleAsync(client, taskId));
    }

    [Fact]
    public async Task TaskArtifact_ForAUrlSubmissionWithoutAHeading_CarriesTheUrlFallback()
    {
        using var fixture = new IngestSubmissionPipelineFixture(
            urlFetchHandler: new StaticContentHandler("Plain text with no heading.", "text/plain"));
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.Url, "https://example.test/article", null, null, null,
            ConvertSteps: new Dictionary<string, bool> { ["markitdown"] = false }));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        Assert.Equal("https://example.test/article", await ArtifactTitleAsync(fixture, taskId));
        Assert.Equal("https://example.test/article", await DetailTitleAsync(client, taskId));
    }

    /// <summary>
    /// A fetch that fails before the manifest exists leaves the chain with nothing but the
    /// task id — and the artifact the Hub writes for that failure must say so too, rather
    /// than carrying an empty label.
    /// </summary>
    [Fact]
    public async Task TaskArtifact_WithoutAManifest_CarriesTheTaskIdFallback()
    {
        // The fixture's default URL handler answers 404, so conversion fails before
        // PersistNormalizedAsync ever writes a manifest.
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.Url, "https://example.test/missing", null, null, null));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "failed");

        Assert.Equal(taskId, await ArtifactTitleAsync(fixture, taskId));
        Assert.Equal(taskId, await DetailTitleAsync(client, taskId));
    }

    /// <summary>
    /// The frontmatter value is a quoted string, so a heading containing the two characters
    /// that can break it — <c>:</c> (key/value separator) and <c>"</c> (the quote itself) —
    /// must survive the write/read round trip through <see cref="TaskArtifactFrontmatter"/>,
    /// which is what the board and detail responses read.
    /// </summary>
    [Fact]
    public async Task TaskArtifact_TitleContainingColonAndQuotes_RoundTripsThroughFrontmatterParsing()
    {
        const string heading = "Config: the \"real\" guide";

        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: new FakeAgentProcessLauncher(autoPlay: false));
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await SubmitMarkdownAsync(fixture, "config.md", $"# {heading}\n\nBody.\n");
        await fixture.WaitForStatusAsync(taskId, s => s == "queued");

        Assert.Equal(heading, await ArtifactTitleAsync(fixture, taskId));
        Assert.Equal(heading, await DetailTitleAsync(client, taskId));

        // The rest of the frontmatter must still parse — an unescaped quote or colon would
        // corrupt the fields that follow, not just the title.
        var frontmatter = await ReadFrontmatterAsync(fixture, taskId);
        Assert.Equal(taskId, frontmatter.TaskId);
        Assert.Equal("queued", frontmatter.Status);
    }

    /// <summary>
    /// The first <c>received</c> write happens before conversion has produced any normalized
    /// markdown, so there is no manifest and therefore no extracted heading yet. Falling back
    /// to the task id at that stage is correct behavior, not a defect — the extracted heading
    /// appears from <c>queued</c> onward, once the manifest exists.
    /// </summary>
    [Fact]
    public async Task TaskArtifact_BeforeConversion_FallsBackBelowTheExtractedHeading()
    {
        using var gate = new GatedContentHandler("# Late Heading\n\nBody.", "text/plain");
        using var fixture = new IngestSubmissionPipelineFixture(
            urlFetchHandler: gate,
            launcher: new FakeAgentProcessLauncher(autoPlay: false));

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.Url, "https://example.test/late", null, null, null,
            ConvertSteps: new Dictionary<string, bool> { ["markitdown"] = false }));

        // Held at `converting`: the fetch is blocked, so no manifest exists yet.
        await fixture.WaitForStatusAsync(taskId, s => s == "converting");
        Assert.Equal(taskId, await ArtifactTitleAsync(fixture, taskId));

        gate.Release();

        await fixture.WaitForStatusAsync(taskId, s => s == "queued");
        Assert.Equal("Late Heading", await ArtifactTitleAsync(fixture, taskId));
    }

    private static async Task<TaskArtifactFrontmatter> ReadFrontmatterAsync(
        IngestSubmissionPipelineFixture fixture, string taskId)
    {
        var markdown = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId));
        return TaskArtifactFrontmatter.TryParse(markdown)
            ?? throw new InvalidOperationException($"Task artifact for '{taskId}' did not parse.");
    }

    private static async Task<string?> ArtifactTitleAsync(IngestSubmissionPipelineFixture fixture, string taskId)
        => (await ReadFrontmatterAsync(fixture, taskId)).Title;

    private static async Task<string> ArtifactStatusAsync(IngestSubmissionPipelineFixture fixture, string taskId)
        => (await ReadFrontmatterAsync(fixture, taskId)).Status;

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

    /// <summary>
    /// Holds the fetch open until the test releases it, so the pipeline can be observed at
    /// <c>converting</c> — a stage that precedes the manifest. The gate is a signal the test
    /// controls, not a wall-clock wait (ADR-021).
    /// </summary>
    private sealed class GatedContentHandler(string content, string contentType) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _released.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await _released.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, contentType),
            };
        }
    }
}
