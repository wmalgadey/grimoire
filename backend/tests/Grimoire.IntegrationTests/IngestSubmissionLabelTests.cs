using System.Text;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;
using Xunit;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #130: a task must carry a label an operator can read from the moment it is on the board,
/// not only once conversion has produced a manifest.
///
/// <para>
/// Every tier of the label chain used to come from the <c>IngestSourceArtifactSet</c> manifest,
/// which <c>IngestSourceArtifactStore.PersistNormalized*</c> writes after the source has been
/// fetched and converted. So a card was labelled with its raw task id for the whole
/// <c>received</c> + <c>converting</c> window — for a URL, an outbound fetch plus a markitdown
/// run, the most visible phase of the task's life — and a submission whose conversion failed
/// never got a manifest at all and stayed id-labelled permanently. The Hub knew the filename
/// or the URL at acceptance the whole time.
/// </para>
/// </summary>
public class IngestSubmissionLabelTests
{
    [Fact]
    public async Task FileSubmission_IsLabelledWithItsFilename_FromTheFirstArtifactWrite()
    {
        using var fixture = new IngestSubmissionPipelineFixture();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "Retention policy draft.md",
            Encoding.UTF8.GetBytes("Body with no heading.\n"), "text/markdown"));

        // Read straight after acceptance: AcceptAsync returns once the `received` artifact is
        // written, with conversion still to run — precisely the window that showed the id.
        var projection = await fixture.BoardStore.GetByTaskIdAsync(fixture.ContentPaths.TasksDir, taskId);

        Assert.NotNull(projection);
        Assert.Equal("Retention policy draft.md", projection!.Title);
        Assert.NotEqual(taskId, projection.Title);
    }

    [Fact]
    public async Task UrlSubmission_IsLabelledWithItsUrl_FromTheFirstArtifactWrite()
    {
        using var fixture = new IngestSubmissionPipelineFixture();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.Url, "https://example.test/retention-policy", null, null, null));

        var projection = await fixture.BoardStore.GetByTaskIdAsync(fixture.ContentPaths.TasksDir, taskId);

        Assert.NotNull(projection);
        Assert.Equal("https://example.test/retention-policy", projection!.Title);
    }

    /// <summary>
    /// The submitted label is a floor, not a ceiling: once conversion extracts a heading, that
    /// is the better label and still wins. This is the ordering that makes the fix safe —
    /// the new tier sits below every manifest tier, so nothing that already had a good label
    /// loses it.
    /// </summary>
    [Fact]
    public async Task ExtractedHeading_SupersedesTheSubmittedFilename_OnceConversionHasRun()
    {
        using var fixture = new IngestSubmissionPipelineFixture();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "upload-42.md",
            Encoding.UTF8.GetBytes("# Retention Policy\n\nBody.\n"), "text/markdown"));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "queued");

        var projection = await fixture.BoardStore.GetByTaskIdAsync(fixture.ContentPaths.TasksDir, taskId);

        Assert.NotNull(projection);
        Assert.Equal("Retention Policy", projection!.Title);
    }
}
