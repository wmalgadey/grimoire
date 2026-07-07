using System.Net;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.Conversion;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T051 (US3) - a corrupted file or an unreachable URL leads to `failed` with a human-readable
/// reason and no partial normalized artifact (SC-003, Acceptance Scenarios 1-2, Edge Cases).
/// </summary>
public class IngestSubmissionFailureTests
{
    [Fact]
    public async Task CorruptedPdfSubmission_ReachesFailed_WithHumanReadableReason_AndNoPartialArtifact()
    {
        // A tiny script standing in for `markitdown` that always fails with a multi-line stderr,
        // mirroring a real tool crash (e.g. a corrupted/password-protected PDF) deterministically,
        // independent of whichever markitdown build/dependencies happen to be installed locally.
        var brokenConverterScript = Path.Combine(Path.GetTempPath(), $"broken-markitdown-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(brokenConverterScript,
            "#!/bin/sh\necho 'Traceback (most recent call last):' >&2\necho '  File \"markitdown\", line 1' >&2\necho 'PdfReadError: unable to parse cross-reference table' >&2\nexit 1\n");
        File.SetUnixFileMode(brokenConverterScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        using var fixture = new IngestSubmissionPipelineFixture(markItDownExecutablePath: brokenConverterScript);

        var bytes = "%PDF-1.4\ncorrupted"u8.ToArray();
        var taskId = await fixture.Pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.PdfFile, null, "corrupted.pdf", bytes, "application/pdf"));

        await fixture.WaitForStatusAsync(taskId, s => s is "completed" or "failed");

        var markdown = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId));
        var final = TaskArtifactFrontmatter.TryParse(markdown);
        Assert.Equal("failed", final!.Status);
        Assert.NotNull(final.FailureReason);
        Assert.DoesNotContain("Traceback", final.FailureReason);
        Assert.Contains("PdfReadError", final.FailureReason);

        Assert.False(File.Exists(fixture.RawPaths.NormalizedMarkdownPathFor(taskId)), "No partial normalized artifact must remain after a failed conversion.");
        Assert.Empty(fixture.Dispatcher.Requests);
    }

    [Fact]
    public async Task UnreachableUrlSubmission_ReachesFailed_WithFetchFailureReason()
    {
        using var handler = new NotFoundHandler();
        using var fixture = new IngestSubmissionPipelineFixture(urlFetchHandler: handler);

        var taskId = await fixture.Pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.Url, "https://example.test/missing", null, null, null));

        await fixture.WaitForStatusAsync(taskId, s => s is "completed" or "failed");

        var markdown = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId));
        var final = TaskArtifactFrontmatter.TryParse(markdown);
        Assert.Equal("failed", final!.Status);
        Assert.Contains("404", final.FailureReason);
        Assert.False(File.Exists(fixture.RawPaths.NormalizedMarkdownPathFor(taskId)));
        Assert.Empty(fixture.Dispatcher.Requests);
    }

    [Theory]
    [InlineData("Traceback (most recent call last):\n  File \"x.py\", line 1\nValueError: bad file\n", "ValueError: bad file")]
    [InlineData("", "Conversion failed for an unknown reason.")]
    [InlineData("single line reason", "single line reason")]
    public void ConversionFailureClassifier_ReturnsLastMeaningfulLine(string raw, string expected)
    {
        Assert.Equal(expected, ConversionFailureClassifier.Classify(raw));
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
