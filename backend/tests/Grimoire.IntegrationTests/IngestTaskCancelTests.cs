using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// Issue #184 remedy (3): <c>POST /{taskId}/cancel</c>, the operator release valve for a
/// run wedged behind an unbounded model call. Before this endpoint existed the only way to
/// free the single-slot queue was <c>docker compose exec hub kill &lt;pid&gt;</c> — not an
/// operator procedure. Cancelling terminates the process and finalizes the task as
/// <c>failed</c> directly, never through the bounded ADR-025 reactivation schedule: a
/// deliberate cancel is not a liveness incident to retry.
/// </summary>
public class IngestTaskCancelTests
{
    [Fact]
    public async Task CancelOfTheActivelyRunningTask_Returns200_TerminatesTheProcess_AndFailsTheTask_WithoutReactivating()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "cancel-me.md",
            Encoding.UTF8.GetBytes("# Cancel Me\n\nBody.\n"), "text/markdown"));

        await PollAsync.WaitAsync(
            () => launcher.Handles.Count >= 1, TimeSpan.FromSeconds(10),
            () => $"Expected at least 1 agent launch, saw {launcher.Handles.Count}.");
        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", taskId);

        var response = await client.PostAsync($"/api/ingest-submissions/{taskId}/cancel", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(taskId, body.GetProperty("taskId").GetString());

        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "failed", TimeSpan.FromSeconds(10));

        Assert.True(handle.Terminated, "Cancel must terminate the agent process.");
        Assert.Null(fixture.Coordinator.RunningTaskId);

        var artifact = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId));
        Assert.Contains("status: failed", artifact, StringComparison.Ordinal);
        Assert.Contains("Cancelled by operator request.", artifact, StringComparison.Ordinal);

        // Never reactivated: exactly the one process this test spawned, no automatic
        // relaunch — a deliberate cancel is not a liveness incident (ADR-025 untouched).
        Assert.Single(launcher.Handles);
        var history = await fixture.Repository.GetStatusHistoryAsync(taskId);
        Assert.DoesNotContain(history, e => e.Status is "liveness_interrupted" or "reactivated");
    }

    [Fact]
    public async Task CancelOfATaskThatIsNotTheRunningOne_Returns409()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = "2026-08-23-ingest-already-completed";
        await IngestTaskDetailHistoryTests.WriteTaskArtifactAsync(fixture, taskId, "completed");

        var response = await client.PostAsync($"/api/ingest-submissions/{taskId}/cancel", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ingest_task_not_running", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task CancelOfAnUnknownTask_Returns404()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var response = await client.PostAsync("/api/ingest-submissions/no-such-task/cancel", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
