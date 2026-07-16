using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T052 (US3) - once the triggered Ingest agent run itself fails, the board surfaces the agent's
/// existing `failure_reason` unchanged; this feature introduces no new failure semantics for the
/// agent phase (Acceptance Scenario 4).
/// </summary>
public class IngestRunFailureVisibilityTests
{
    [Fact]
    public async Task AgentRunFailure_PassesThroughAgentsOwnFailureReason_Unchanged()
    {
        const string agentFailureReason = "Guardrail denial: write_file outside allowed policy scope";
        var dispatcher = new FakeAgentProcessLauncher(terminalStatus: "failed", failureReason: agentFailureReason);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: dispatcher);

        var bytes = System.Text.Encoding.UTF8.GetBytes("# Note\n\nContent.");
        var taskId = await fixture.Pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.MarkdownFile, null, "note.md", bytes, "text/markdown"));

        await fixture.WaitForStatusAsync(taskId, s => s == "failed");
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "failed");

        var markdown = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId));
        var final = TaskArtifactFrontmatter.TryParse(markdown);
        Assert.Equal("failed", final!.Status);
        Assert.Equal(agentFailureReason, final.FailureReason);

        var failedEvent = fixture.PublishedEvents.Single(e => e.TaskId == taskId && e.ToStatus == "failed");
        Assert.Equal(agentFailureReason, failedEvent.FailureReason);
        Assert.Equal("running", failedEvent.FromStatus);

        // The board must observe the full journey through to the agent-owned outcome (SC-007).
        var stages = fixture.PublishedEvents.Where(e => e.TaskId == taskId).Select(e => e.ToStatus).ToList();
        Assert.Equal(["received", "converting", "queued", "running", "failed"], stages);
    }

    /// <summary>
    /// Regression test (review finding): if the dispatcher itself throws (e.g. the child process
    /// could not be started, or IngestAgentDispatcher throws on a crash) rather than completing
    /// and writing its own terminal artifact, the task must still reach `failed` — not remain
    /// stuck in `running` — and the exception must not go unobserved (TriggerAsync is
    /// fire-and-forget).
    /// </summary>
    [Fact]
    public async Task DispatcherThrows_StillReachesFailed_AndCleansUpOperationalState()
    {
        var dispatcher = new FakeAgentProcessLauncher(throwOnStart: new InvalidOperationException("Failed to start ingest agent process."));
        using var fixture = new IngestSubmissionPipelineFixture(launcher: dispatcher);

        var bytes = System.Text.Encoding.UTF8.GetBytes("# Note\n\nContent.");
        var taskId = await fixture.Pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.MarkdownFile, null, "note.md", bytes, "text/markdown"));

        await fixture.WaitForStatusAsync(taskId, s => s == "failed", TimeSpan.FromSeconds(10));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "failed", TimeSpan.FromSeconds(10));

        var markdown = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId));
        var final = TaskArtifactFrontmatter.TryParse(markdown);
        Assert.Equal("failed", final!.Status);
        Assert.Contains("Failed to start ingest agent process.", final.FailureReason);
    }
}
