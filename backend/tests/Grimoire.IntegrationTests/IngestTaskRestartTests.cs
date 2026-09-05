using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.OperationalState;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T028 (023-task-ui-improvements, US5 / FR-010..FR-013, SC-007, SC-008): manual restart of
/// a finally-failed task from the UI. Race-safe under concurrent duplicate requests (CAS on
/// the persisted operational-task-state row, ADR-018's idiom), prior history preserved.
/// Contract: contracts/http-api.md "New: POST /{taskId}/restart".
/// </summary>
public class IngestTaskRestartTests
{
    [Fact]
    public async Task RestartOfAFailedTask_Returns202_AppendsRestartedAndQueued_ResetsAttempt_AndReRuns()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await FailATaskAsync(fixture, launcher);
        var historyBeforeRestart = await fixture.Repository.GetIngestStatusHistoryAsync(taskId);

        var response = await client.PostAsync($"/api/ingest-submissions/{taskId}/restart", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(taskId, body.GetProperty("taskId").GetString());
        Assert.Equal("queued", body.GetProperty("status").GetString());

        var secondHandle = await WaitForNthHandleAsync(launcher, 2);
        secondHandle.EmitEvent("started", taskId);
        secondHandle.EmitEvent("completed", taskId, new { summary = "Restarted run completed." });

        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        var historyAfterRestart = await fixture.Repository.GetIngestStatusHistoryAsync(taskId);

        // Prior failure entries are retained (FR-013) — never truncated.
        Assert.Equal(
            historyBeforeRestart.Select(h => (h.Seq, h.Status)),
            historyAfterRestart.Take(historyBeforeRestart.Count).Select(h => (h.Seq, h.Status)));

        var afterFailure = historyAfterRestart.Skip(historyBeforeRestart.Count).Select(h => h.Status).ToList();
        Assert.Equal(["restarted", "queued", "running", "completed"], afterFailure);

        // Runs under the same task id — no new task was created.
        Assert.Equal(taskId, historyAfterRestart[^1].TaskId);
    }

    [Fact]
    public async Task RestartOfANonFailedTask_Returns409_WithReason()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = "2026-08-13-ingest-running";
        await IngestTaskDetailHistoryTests.WriteTaskArtifactAsync(fixture, taskId, "running");

        var response = await client.PostAsync($"/api/ingest-submissions/{taskId}/restart", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // ADR-025's two declines are now distinguishable: this one is "not failed", not the
        // "source missing" case, and the caller can tell them apart (ADR-018's rule that the
        // caller sees the actual outcome). Before 024 both arrived as an untyped prose string.
        Assert.Equal("restart_task_not_failed", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task RestartOfAnUnknownTask_Returns404()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var response = await client.PostAsync("/api/ingest-submissions/no-such-task/restart", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RestartWhenTheNormalizedSourceIsMissing_Returns409()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await FailATaskAsync(fixture, launcher);
        var manifest = await fixture.SourceArtifactStore.TryReadMetadataAsync(taskId);
        File.Delete(manifest!.NormalizedMarkdownPath);

        var response = await client.PostAsync($"/api/ingest-submissions/{taskId}/restart", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentDuplicateRestarts_ExactlyOneWins_OneRestartedRow_OneQueueInsertion()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = await FailATaskAsync(fixture, launcher);

        const int concurrentRequests = 8;
        var responses = await Task.WhenAll(Enumerable.Range(0, concurrentRequests)
            .Select(_ => client.PostAsync($"/api/ingest-submissions/{taskId}/restart", content: null)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Accepted);
        Assert.Equal(concurrentRequests - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var history = await fixture.Repository.GetIngestStatusHistoryAsync(taskId);
        Assert.Single(history, h => h.Status == "restarted");

        // Exactly one queue insertion: with no other task occupying the slot, the winning
        // restart is dequeued and started immediately — so the task is either still queued
        // or has already become the running task, never both, never neither.
        var queued = await fixture.Repository.GetQueuedIngestRunsAsync();
        var stillQueued = queued.Count(q => q.TaskId == taskId);
        var isRunning = fixture.Coordinator.RunningTaskId == taskId ? 1 : 0;
        Assert.Equal(1, stillQueued + isRunning);
    }

    /// <summary>
    /// Drives one task through a real submission to a scripted `failed` terminal event.
    /// The real agent process owns the artifact write on its own terminal event
    /// (ADR-002) — this scripted handle only emits the NDJSON event, so the helper writes
    /// the artifact itself afterward, to reach the same observable "failed" state the
    /// restart endpoint's precondition check reads from.
    /// </summary>
    private static async Task<string> FailATaskAsync(IngestSubmissionPipelineFixture fixture, FakeAgentProcessLauncher launcher)
    {
        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "restart-me.md",
            Encoding.UTF8.GetBytes("# Restart Me\n\nBody.\n"), "text/markdown"));

        var handle = await WaitForNthHandleAsync(launcher, 1);
        handle.EmitEvent("started", taskId);
        handle.EmitEvent("failed", taskId, new { reason = "Agent run failed." });

        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "failed");
        await IngestTaskDetailHistoryTests.WriteTaskArtifactAsync(fixture, taskId, "failed", failureReason: "Agent run failed.");
        return taskId;
    }

    private static Task<ScriptedAgentProcessHandle> WaitForNthHandleAsync(FakeAgentProcessLauncher launcher, int n)
    {
        return PollAsync.WaitAsync(
            () => launcher.Handles.Count >= n, TimeSpan.FromSeconds(10),
            () => $"Expected at least {n} agent launches, saw {launcher.Handles.Count}.")
            .ContinueWith(_ => launcher.Handles[n - 1]);
    }
}
