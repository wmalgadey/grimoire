using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Hub.OperationalState;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T007 (023-task-ui-improvements, US1 / FR-006, SC-004): the task detail endpoint serves the
/// full ordered status history so a failed task's stopping point is identifiable, and answers
/// with an empty array — never an error — for a task with no recorded transitions.
/// Contract: contracts/http-api.md "Changed: GET /api/ingest-submissions/{taskId}".
/// </summary>
public class IngestTaskDetailHistoryTests
{
    [Fact]
    public async Task GetTaskDetail_ReturnsStatusHistory_OrderedBySeq_WithStatusEnteredAtAndDetail()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = "2026-08-13-ingest-history01";
        await WriteTaskArtifactAsync(fixture, taskId, "failed", failureReason: "Agent run failed.");

        await fixture.Repository.AppendIngestStatusHistoryAsync(taskId, "received", DateTimeOffset.Parse("2026-08-13T07:00:01Z"));
        await fixture.Repository.AppendIngestStatusHistoryAsync(taskId, "converting", DateTimeOffset.Parse("2026-08-13T07:00:02Z"));
        await fixture.Repository.AppendIngestStatusHistoryAsync(taskId, "queued", DateTimeOffset.Parse("2026-08-13T07:00:05Z"));
        await fixture.Repository.AppendIngestStatusHistoryAsync(taskId, "running", DateTimeOffset.Parse("2026-08-13T07:00:06Z"));
        await fixture.Repository.AppendIngestStatusHistoryAsync(
            taskId, IngestHistoryStatuses.LivenessInterrupted, DateTimeOffset.Parse("2026-08-13T07:01:06Z"),
            "attempt 1; next retry in 10s");
        await fixture.Repository.AppendIngestStatusHistoryAsync(
            taskId, "failed", DateTimeOffset.Parse("2026-08-13T07:05:00Z"), "Agent run failed.");

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/ingest-submissions/{taskId}");
        var history = detail.GetProperty("statusHistory").EnumerateArray().ToList();

        Assert.Equal(
            ["received", "converting", "queued", "running", "liveness_interrupted", "failed"],
            history.Select(e => e.GetProperty("status").GetString()));

        Assert.Equal(
            DateTimeOffset.Parse("2026-08-13T07:00:01Z"),
            history[0].GetProperty("enteredAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, history[0].GetProperty("detail").ValueKind);
        Assert.Equal("attempt 1; next retry in 10s", history[4].GetProperty("detail").GetString());

        // The failing entry is the last one — that is what makes the stopping point readable.
        Assert.Equal("failed", history[^1].GetProperty("status").GetString());
    }

    /// <summary>
    /// A task with no recorded transitions answers with an empty array rather than an error:
    /// the field is always present, so the client renders an empty path instead of branching
    /// on its absence. (Not a legacy path — the alpha carries no pre-feature tasks.)
    /// </summary>
    [Fact]
    public async Task GetTaskDetail_ForTaskWithoutHistoryRows_ReturnsEmptyArray()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = "2026-08-13-ingest-nohistory";
        await WriteTaskArtifactAsync(fixture, taskId, "completed");

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/ingest-submissions/{taskId}");

        var history = detail.GetProperty("statusHistory");
        Assert.Equal(JsonValueKind.Array, history.ValueKind);
        Assert.Equal(0, history.GetArrayLength());
    }

    [Fact]
    public async Task GetTaskDetail_HistoryReflectsARealRun_EndToEnd()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var markdown = "# Detail history\n\nBody.\n"u8.ToArray();
        var taskId = await fixture.Pipeline.AcceptAsync(new(
            Grimoire.Domain.Ingest.IngestSubmissionKind.MarkdownFile,
            Url: null, FileName: "detail-history.md", FileBytes: markdown, FileContentType: "text/markdown"));

        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/ingest-submissions/{taskId}");
        Assert.Equal(
            ["received", "converting", "queued", "running", "completed"],
            detail.GetProperty("statusHistory").EnumerateArray().Select(e => e.GetProperty("status").GetString()));
    }

    internal static async Task WriteTaskArtifactAsync(
        IngestSubmissionPipelineFixture fixture, string taskId, string status,
        string? sourceRef = null, string? originalRef = null, string? failureReason = null)
    {
        Directory.CreateDirectory(fixture.ContentPaths.TasksDir);
        var reason = failureReason is null ? "null" : $"\"{failureReason}\"";
        var source = sourceRef is null ? "null" : $"\"{sourceRef}\"";
        var original = originalRef is null ? "null" : $"\"{originalRef}\"";

        await File.WriteAllTextAsync(
            Path.Combine(fixture.ContentPaths.TasksDir, $"{taskId}.md"),
            $"""
            ---
            task_id: {taskId}
            type: ingest
            status: {status}
            agent: ingest
            started_at: 2026-08-13T07:00:00.0000000Z
            completed_at: {(status is "completed" or "failed" ? "2026-08-13T07:05:00.0000000Z" : "null")}
            source_ref: {source}
            original_ref: {original}
            pages_touched: []
            failure_reason: {reason}
            ---

            Task artifact fixture.
            """);
    }
}
