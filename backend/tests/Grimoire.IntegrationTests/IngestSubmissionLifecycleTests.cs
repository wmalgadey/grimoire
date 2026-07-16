using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T020 (US1) - full lifecycle received→converting→queued→running→completed proceeds
/// automatically with no user action, using a fake dispatcher (SC-001, SC-006, SC-007).
/// </summary>
public class IngestSubmissionLifecycleTests
{
    [Fact]
    public async Task MarkdownFileSubmission_ProgressesThroughFullLifecycle_ToCompleted()
    {
        using var fixture = new IngestSubmissionPipelineFixture();

        var bytes = System.Text.Encoding.UTF8.GetBytes("# Hello\n\nSome content.");
        var taskId = await fixture.Pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.MarkdownFile, null, "note.md", bytes, "text/markdown"));

        Assert.NotNull(taskId);

        // Immediately after AcceptAsync returns, the task must already be visible (SC-001).
        var immediate = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId));
        var immediateStatus = Grimoire.Hub.IngestSubmission.TaskArtifactFrontmatter.TryParse(immediate);
        Assert.NotNull(immediateStatus);
        Assert.Contains(immediateStatus!.Status, new[] { "received", "converting", "queued", "running", "completed" });

        await fixture.WaitForStatusAsync(taskId, s => s is "completed" or "failed");
        // The file can reach "completed" slightly before the matching realtime event is published
        // (TriggerAsync still has to delete the operational-state row and re-read the file first).
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        var finalMarkdown = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId));
        var final = Grimoire.Hub.IngestSubmission.TaskArtifactFrontmatter.TryParse(finalMarkdown);
        Assert.NotNull(final);
        Assert.Equal("completed", final!.Status);

        // The agent was triggered against the persisted normalized markdown, not re-fetched (FR-010).
        var request = Assert.Single(fixture.Launcher.Requests);
        Assert.Equal(taskId, request.TaskId);
        Assert.Equal("file", request.SourceKind);
        Assert.True(File.Exists(request.SourceRef));

        // Board must observe every stage without filesystem inspection (SC-004, SC-007).
        var observedStages = fixture.PublishedEvents.Where(e => e.TaskId == taskId).Select(e => e.ToStatus).ToList();
        Assert.Contains("received", observedStages);
        Assert.Contains("converting", observedStages);
        Assert.Contains("queued", observedStages);
        Assert.Contains("running", observedStages);
        Assert.Contains("completed", observedStages);
    }

    [Fact]
    public async Task UrlSubmission_ThatFailsToFetch_ReachesFailedWithoutTriggeringAgent()
    {
        using var fixture = new IngestSubmissionPipelineFixture();

        var taskId = await fixture.Pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.Url, "https://example.invalid/unreachable", null, null, null));

        await fixture.WaitForStatusAsync(taskId, s => s is "completed" or "failed");

        var finalMarkdown = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId));
        var final = Grimoire.Hub.IngestSubmission.TaskArtifactFrontmatter.TryParse(finalMarkdown);
        Assert.Equal("failed", final!.Status);
        Assert.NotNull(final.FailureReason);

        // The agent must never be triggered for a submission that failed before reaching queued.
        Assert.Empty(fixture.Launcher.Requests);
    }
}
