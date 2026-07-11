using System.Net;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T021 (US1) - a URL submission is fetched, converted (via the real `markitdown` CLI), and
/// persisted as both the original and normalized artifacts (SC-002).
/// </summary>
public class SourceArtifactPersistenceTests
{
    [Fact]
    public async Task UrlSubmission_PersistsOriginalAndNormalizedArtifacts()
    {
        const string html = "<html><body><h1>Fixture Article</h1><p>Fixture body text.</p></body></html>";
        using var handler = new StaticResponseHandler(html, "text/html");
        using var fixture = new IngestSubmissionPipelineFixture(urlFetchHandler: handler);

        var taskId = await fixture.Pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.Url, "https://example.test/article", null, null, null));

        await fixture.WaitForStatusAsync(taskId, s => s is "completed" or "failed");

        var artifactSet = await fixture.SourceArtifactStore.TryReadMetadataAsync(taskId);
        Assert.NotNull(artifactSet);
        Assert.True(File.Exists(artifactSet!.OriginalPath), "Original artifact must be persisted under raw/originals.");
        Assert.True(File.Exists(artifactSet.NormalizedMarkdownPath), "Normalized artifact must be persisted under raw/sources.");
        Assert.StartsWith(fixture.RawPaths.OriginalsDir, artifactSet.OriginalPath);
        Assert.StartsWith(fixture.RawPaths.SourcesDir, artifactSet.NormalizedMarkdownPath);

        var normalizedContent = await File.ReadAllTextAsync(artifactSet.NormalizedMarkdownPath);
        Assert.Contains("Fixture Article", normalizedContent);
        Assert.Contains("Fixture body text", normalizedContent);

        // The dispatched agent request must reference the persisted normalized artifact, not the URL (FR-010).
        var request = Assert.Single(fixture.Dispatcher.Requests);
        Assert.Equal(artifactSet.NormalizedMarkdownPath, request.SourceRef);

        // Regression (review finding): `content_type` must be an actual MIME type, not a file extension.
        var persistedEntry = Assert.Single(fixture.Logger.Entries, e => e.EventName == "ingest.submission.original.persisted");
        Assert.Equal("text/html", persistedEntry.Fields["content_type"]);
    }

    private sealed class StaticResponseHandler(string body, string mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType),
            };
            return Task.FromResult(response);
        }
    }
}
