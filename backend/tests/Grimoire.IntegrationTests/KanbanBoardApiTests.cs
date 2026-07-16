using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T039 (US2) - the board projection (backing `GET /api/ingest-submissions`) lists every accepted
/// task exactly once, grouped by its current stage (FR-007, Acceptance Scenario 1).
/// </summary>
public class KanbanBoardApiTests
{
    [Fact]
    public async Task GetAll_ListsEveryAcceptedTask_ExactlyOnce_GroupedByStage()
    {
        var dispatcher = new FakeAgentProcessLauncher();
        using var fixture = new IngestSubmissionPipelineFixture(launcher: dispatcher);

        var taskA = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "a.md", System.Text.Encoding.UTF8.GetBytes("# A"), "text/markdown"));
        var taskB = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "b.md", System.Text.Encoding.UTF8.GetBytes("# B"), "text/markdown"));

        await fixture.WaitForStatusAsync(taskA, s => s is "completed" or "failed");
        await fixture.WaitForStatusAsync(taskB, s => s is "completed" or "failed");

        var board = await fixture.BoardStore.GetAllAsync(fixture.ContentPaths.TasksDir);

        Assert.Equal(2, board.Count);
        Assert.Single(board, t => t.TaskId == taskA);
        Assert.Single(board, t => t.TaskId == taskB);
        Assert.All(board, t => Assert.Equal("completed", t.Column));
        Assert.All(board, t => Assert.Equal($"/api/ingest-submissions/{t.TaskId}", t.TaskLink));
    }

    [Fact]
    public async Task GetByTaskId_ReturnsNull_ForUnknownTask()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        var result = await fixture.BoardStore.GetByTaskIdAsync(fixture.ContentPaths.TasksDir, "does-not-exist");
        Assert.Null(result);
    }
}
