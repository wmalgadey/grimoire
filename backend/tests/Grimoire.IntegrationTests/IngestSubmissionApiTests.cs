using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T019 (US1) - contract shape for ingest submission (contracts/ingest-submission-api.md):
/// URL (JSON) and file (multipart) variants are both accepted immediately with a non-terminal
/// status, and validation failures map to the declared 400/415/422 categories.
/// </summary>
public class IngestSubmissionApiTests
{
    [Fact]
    public void ValidateUrl_AcceptsAbsoluteHttpUrl()
    {
        var validator = new IngestSubmissionValidator();
        var result = validator.ValidateUrl("https://example.test/article");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateUrl_RejectsMissingUrl_AsBadRequest()
    {
        var validator = new IngestSubmissionValidator();
        var result = validator.ValidateUrl(null);
        Assert.False(result.IsValid);
        Assert.Equal(IngestSubmissionValidationErrorKind.BadRequest, result.ErrorKind);
    }

    [Fact]
    public void ValidateUrl_RejectsMalformedUrl_AsUnprocessableEntity()
    {
        var validator = new IngestSubmissionValidator();
        var result = validator.ValidateUrl("not-a-url");
        Assert.False(result.IsValid);
        Assert.Equal(IngestSubmissionValidationErrorKind.UnprocessableEntity, result.ErrorKind);
    }

    [Theory]
    [InlineData(IngestSubmissionKind.MarkdownFile, "notes.md")]
    [InlineData(IngestSubmissionKind.PdfFile, "report.pdf")]
    [InlineData(IngestSubmissionKind.OfficeFile, "deck.pptx")]
    public void ValidateFile_AcceptsMatchingExtension(IngestSubmissionKind kind, string fileName)
    {
        var validator = new IngestSubmissionValidator();
        var result = validator.ValidateFile(kind, fileName, sizeBytes: 128);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateFile_RejectsUnsupportedExtension_AsUnsupportedMediaType()
    {
        var validator = new IngestSubmissionValidator();
        var result = validator.ValidateFile(IngestSubmissionKind.PdfFile, "report.exe", sizeBytes: 128);
        Assert.False(result.IsValid);
        Assert.Equal(IngestSubmissionValidationErrorKind.UnsupportedMediaType, result.ErrorKind);
    }

    [Fact]
    public void ValidateFile_RejectsEmptyFile_AsUnprocessableEntity()
    {
        var validator = new IngestSubmissionValidator();
        var result = validator.ValidateFile(IngestSubmissionKind.MarkdownFile, "empty.md", sizeBytes: 0);
        Assert.False(result.IsValid);
        Assert.Equal(IngestSubmissionValidationErrorKind.UnprocessableEntity, result.ErrorKind);
    }

    [Theory]
    [InlineData("url", true)]
    [InlineData("markdown_file", true)]
    [InlineData("pdf_file", true)]
    [InlineData("office_file", true)]
    [InlineData("something_else", false)]
    [InlineData(null, false)]
    public void TryParseKind_AcceptsOnlyDeclaredKinds(string? raw, bool expected)
    {
        Assert.Equal(expected, IngestSubmissionValidator.TryParseKind(raw, out _));
    }

    [Fact]
    public async Task AcceptAsync_UrlSubmission_ReturnsTaskIdImmediately_WithNonTerminalStatus()
    {
        // A working fetch response + a slow fake run keeps the pipeline from reaching a terminal
        // state before this test reads the file, so the assertion below is deterministic rather
        // than racing the background task.
        using var handler = new SucceedingHandler();
        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromSeconds(2)),
            urlFetchHandler: handler);

        var taskId = await fixture.Pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.Url, "https://example.test/article", null, null, null));

        Assert.False(string.IsNullOrWhiteSpace(taskId));

        var markdown = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId));
        var frontmatter = TaskArtifactFrontmatter.TryParse(markdown);
        Assert.NotNull(frontmatter);
        Assert.NotEqual("completed", frontmatter!.Status);
        Assert.NotEqual("failed", frontmatter.Status);
    }

    [Fact]
    public async Task AcceptAsync_FileSubmission_ReturnsTaskIdImmediately_WithNonTerminalStatus()
    {
        using var fixture = new IngestSubmissionPipelineFixture(launcher: new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromSeconds(2)));
        var bytes = System.Text.Encoding.UTF8.GetBytes("# Note\n\nContent.");

        var taskId = await fixture.Pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.MarkdownFile, null, "note.md", bytes, "text/markdown"));

        var markdown = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId));
        var frontmatter = TaskArtifactFrontmatter.TryParse(markdown);
        Assert.NotNull(frontmatter);
        Assert.NotEqual("completed", frontmatter!.Status);
        Assert.NotEqual("failed", frontmatter.Status);
    }

    private sealed class SucceedingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body><p>Fixture</p></body></html>", System.Text.Encoding.UTF8, "text/html"),
            };
            return Task.FromResult(response);
        }
    }
}
