using System.Net;
using System.Security.Cryptography;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T025 (US3) — convert-step configuration (FR-011..FR-015, SC-004): registry
/// validation rejects before task creation, disabling the step stores content
/// byte-identical, defaults reproduce 003 behavior, and the applied configuration is
/// recorded on the artifact.
/// </summary>
public class ConvertStepTests
{
    [Fact]
    public void Validator_RejectsUnknownStep_NotApplicableStep_AndRequiredStepDisabled()
    {
        var validator = new IngestSubmissionValidator();

        var unknown = validator.ValidateConvertSteps("url", new Dictionary<string, bool> { ["foo"] = false });
        Assert.False(unknown.IsValid);
        Assert.Contains("unknown_convert_step", unknown.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(IngestSubmissionValidationErrorKind.BadRequest, unknown.ErrorKind);

        var notApplicable = validator.ValidateConvertSteps("markdown_file", new Dictionary<string, bool> { ["markitdown"] = false });
        Assert.False(notApplicable.IsValid);
        Assert.Contains("convert_step_not_applicable", notApplicable.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(IngestSubmissionValidationErrorKind.BadRequest, notApplicable.ErrorKind);

        foreach (var binaryKind in new[] { "pdf_file", "office_file" })
        {
            var requiredDisabled = validator.ValidateConvertSteps(binaryKind, new Dictionary<string, bool> { ["markitdown"] = false });
            Assert.False(requiredDisabled.IsValid);
            Assert.Contains("convert_step_required", requiredDisabled.ErrorMessage, StringComparison.Ordinal);
            Assert.Equal(IngestSubmissionValidationErrorKind.UnprocessableEntity, requiredDisabled.ErrorKind);
        }

        // Enabled (or absent) configurations are always valid.
        Assert.True(validator.ValidateConvertSteps("pdf_file", new Dictionary<string, bool> { ["markitdown"] = true }).IsValid);
        Assert.True(validator.ValidateConvertSteps("url", null).IsValid);
    }

    [Fact]
    public async Task UrlSubmission_WithConversionDisabled_StoresFetchedContentByteIdentical()
    {
        var htmlBytes = "<html><body><h1>Rohtext äöü</h1><p>Bleibt exakt erhalten.</p></body></html>"u8.ToArray();
        using var handler = new StaticBytesHandler(htmlBytes, "text/html");
        using var fixture = new IngestSubmissionPipelineFixture(urlFetchHandler: handler);

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.Url, "https://example.test/raw", null, null, null,
            ConvertSteps: new Dictionary<string, bool> { ["markitdown"] = false }));

        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus is "completed" or "failed", TimeSpan.FromSeconds(15));

        var artifactSet = await fixture.SourceArtifactStore.TryReadMetadataAsync(taskId);
        Assert.NotNull(artifactSet);

        // SC-004: the normalized artifact is byte-identical to what was received,
        // checksum computed over the unmodified bytes.
        var storedBytes = await File.ReadAllBytesAsync(artifactSet!.NormalizedMarkdownPath);
        Assert.Equal(htmlBytes, storedBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(htmlBytes)).ToLowerInvariant(), artifactSet.NormalizedChecksum);

        // The agent consumes the as-received artifact (FR-012).
        var request = Assert.Single(fixture.Launcher.Requests);
        Assert.Equal(artifactSet.NormalizedMarkdownPath, request.SourceRef);

        // Applied configuration is recorded on the task artifact (FR-014).
        var artifact = TaskArtifactFrontmatter.TryParse(await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId)));
        Assert.NotNull(artifact?.ConvertSteps);
        Assert.False(artifact!.ConvertSteps!["markitdown"]);

        // convert_config log at acceptance + disabled metric path (SC-003).
        var entry = Assert.Single(fixture.Logger.Entries, e => e.EventName == "ingest.submission.convert_config");
        Assert.Equal("markitdown", entry.Fields["step"]);
        Assert.Equal(false, entry.Fields["enabled"]);
    }

    [Fact]
    public async Task DefaultSubmission_RecordsEnabledConfiguration_AndBehavesLike003()
    {
        var htmlBytes = "<html><body><h1>Converted Fixture</h1><p>Body text.</p></body></html>"u8.ToArray();
        using var handler = new StaticBytesHandler(htmlBytes, "text/html");
        using var fixture = new IngestSubmissionPipelineFixture(urlFetchHandler: handler);

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.Url, "https://example.test/article", null, null, null));

        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus is "completed" or "failed", TimeSpan.FromSeconds(30));

        // FR-015: default path converts (real markitdown), exactly as specified in 003.
        var artifactSet = await fixture.SourceArtifactStore.TryReadMetadataAsync(taskId);
        Assert.NotNull(artifactSet);
        var normalized = await File.ReadAllTextAsync(artifactSet!.NormalizedMarkdownPath);
        Assert.Contains("Converted Fixture", normalized, StringComparison.Ordinal);
        Assert.NotEqual(htmlBytes, await File.ReadAllBytesAsync(artifactSet.NormalizedMarkdownPath));

        var artifact = TaskArtifactFrontmatter.TryParse(await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId)));
        Assert.NotNull(artifact?.ConvertSteps);
        Assert.True(artifact!.ConvertSteps!["markitdown"]);
    }

    private sealed class StaticBytesHandler(byte[] body, string mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
