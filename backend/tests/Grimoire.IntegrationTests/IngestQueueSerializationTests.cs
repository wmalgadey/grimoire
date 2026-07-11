using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T022 (US1) - a second submission reaching `queued` while a run is in progress waits and
/// auto-triggers with no user action once the prior run reaches a terminal state
/// (FR-012, FR-013, SC-006, quickstart.md Scenario 4).
/// </summary>
public class IngestQueueSerializationTests
{
    [Fact]
    public async Task SecondSubmission_WaitsForFirstRunToFinish_ThenAutoTriggers()
    {
        var dispatcher = new FakeIngestAgentDispatcher(simulatedRunDuration: TimeSpan.FromMilliseconds(300));
        using var fixture = new IngestSubmissionPipelineFixture(dispatcher: dispatcher);

        var bytesA = System.Text.Encoding.UTF8.GetBytes("# First\n\nFirst content.");
        var bytesB = System.Text.Encoding.UTF8.GetBytes("# Second\n\nSecond content.");

        // Both submissions are accepted immediately; neither blocks the other's acceptance (FR-012).
        var taskA = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(IngestSubmissionKind.MarkdownFile, null, "a.md", bytesA, "text/markdown"));
        var taskB = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(IngestSubmissionKind.MarkdownFile, null, "b.md", bytesB, "text/markdown"));

        await fixture.WaitForStatusAsync(taskA, s => s is "completed" or "failed", TimeSpan.FromSeconds(15));
        await fixture.WaitForStatusAsync(taskB, s => s is "completed" or "failed", TimeSpan.FromSeconds(15));

        Assert.Equal(2, dispatcher.RunWindows.Count);
        var (firstStart, firstEnd) = dispatcher.RunWindows[0];
        var (secondStart, secondEnd) = dispatcher.RunWindows[1];

        // The single-concurrent-run constraint (FR-013): the two dispatched runs must never overlap.
        Assert.True(secondStart >= firstEnd, $"Expected the second run ({secondStart:O}) to start no earlier than the first run finished ({firstEnd:O}).");
    }
}
