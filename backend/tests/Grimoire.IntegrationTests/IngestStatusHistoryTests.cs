using Grimoire.Hub.OperationalState;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T006 (023-task-ui-improvements, Phase 2 / FR-005, ADR-025): every lifecycle transition
/// the Hub publishes is durably recorded as an ordered, append-only history row. State-based
/// throughout (Principle II): the assertions read rows back out of the real SQLite file the
/// production code wrote them to — no interaction verification on the publisher.
/// </summary>
public class IngestStatusHistoryTests
{
    [Fact]
    public async Task FullLifecycle_AppendsOrderedHistory_ThatSurvivesFinishRun()
    {
        using var fixture = new IngestSubmissionPipelineFixture();

        await fixture.Coordinator.EnqueueAsync("task-history", Path.Combine(fixture.Root, "src.md"), null);
        await fixture.WaitForPublishedEventAsync("task-history", e => e.ToStatus == "completed");

        var history = await fixture.Repository.GetIngestStatusHistoryAsync("task-history");

        // FinishRunAsync deletes the transient operational row; history must outlive it.
        Assert.Null(await fixture.Repository.GetIngestTaskStateByTaskIdAsync("task-history"));
        Assert.Equal(["running", "completed"], history.Select(h => h.Status));
        Assert.Equal([1L, 2L], history.Select(h => h.Seq));
    }

    [Fact]
    public async Task SubmissionThroughCompletion_RecordsEveryStage_InEnteredOrder()
    {
        using var fixture = new IngestSubmissionPipelineFixture();

        var markdown = "# Ordered lifecycle\n\nBody.\n"u8.ToArray();
        var taskId = await fixture.Pipeline.AcceptAsync(new(
            Grimoire.Domain.Ingest.IngestSubmissionKind.MarkdownFile,
            Url: null, FileName: "ordered.md", FileBytes: markdown, FileContentType: "text/markdown"));

        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        var history = await fixture.Repository.GetIngestStatusHistoryAsync(taskId);

        Assert.Equal(
            ["received", "converting", "queued", "running", "completed"],
            history.Select(h => h.Status));
        Assert.Equal([1L, 2L, 3L, 4L, 5L], history.Select(h => h.Seq));
        Assert.All(history, entry => Assert.Equal(taskId, entry.TaskId));
        // Non-decreasing timestamps: the "path" is only readable if it is chronological.
        Assert.Equal(
            history.Select(h => h.EnteredAt).OrderBy(t => t),
            history.Select(h => h.EnteredAt));
    }

    [Fact]
    public async Task FailedRun_RecordsFailureEntry_WithReasonAsDetail()
    {
        var launcher = new FakeAgentProcessLauncher(terminalStatus: "failed", failureReason: "Turn cap exceeded.");
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);

        await fixture.Coordinator.EnqueueAsync("task-failing", Path.Combine(fixture.Root, "src.md"), null);
        await fixture.WaitForPublishedEventAsync("task-failing", e => e.ToStatus == "failed");

        var history = await fixture.Repository.GetIngestStatusHistoryAsync("task-failing");
        var failure = Assert.Single(history, h => h.Status == "failed");
        Assert.Equal("Turn cap exceeded.", failure.Detail);
        Assert.Equal(history[^1].Seq, failure.Seq);
    }

    [Fact]
    public async Task History_SurvivesHubRestart_AndKeepsAppendingFromTheSameSequence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-history-restart-{Guid.NewGuid():N}");
        try
        {
            using (var fixture = new IngestSubmissionPipelineFixture(root: root))
            {
                await fixture.Coordinator.EnqueueAsync("task-restarted-hub", Path.Combine(root, "src.md"), null);
                await fixture.WaitForPublishedEventAsync("task-restarted-hub", e => e.ToStatus == "completed");
            }

            // A second fixture over the same root is a fresh Hub process against the same
            // database file: InitializeAsync runs again, and nothing may be lost or reset.
            var repository = new OperationalStateRepository(Path.Combine(root, "operational-state.db"));
            await repository.InitializeAsync();

            var afterRestart = await repository.GetIngestStatusHistoryAsync("task-restarted-hub");
            Assert.Equal(["running", "completed"], afterRestart.Select(h => h.Status));

            var seq = await repository.AppendIngestStatusHistoryAsync(
                "task-restarted-hub", IngestHistoryStatuses.Restarted, DateTimeOffset.UtcNow, "manual restart");
            Assert.Equal(afterRestart[^1].Seq + 1, seq);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task History_IsPerTask_AndEachTaskNumbersItsOwnSequenceFromOne()
    {
        using var fixture = new IngestSubmissionPipelineFixture();

        await fixture.Repository.AppendIngestStatusHistoryAsync("task-a", "received", DateTimeOffset.UtcNow);
        await fixture.Repository.AppendIngestStatusHistoryAsync("task-b", "received", DateTimeOffset.UtcNow);
        await fixture.Repository.AppendIngestStatusHistoryAsync("task-a", "converting", DateTimeOffset.UtcNow);

        Assert.Equal([1L, 2L], (await fixture.Repository.GetIngestStatusHistoryAsync("task-a")).Select(h => h.Seq));
        Assert.Equal([1L], (await fixture.Repository.GetIngestStatusHistoryAsync("task-b")).Select(h => h.Seq));
    }

    /// <summary>
    /// Concurrent appends for the same task must all survive. <c>seq</c> is derived from the
    /// task's own current maximum, so writers that read that maximum before either of them
    /// inserts would compute the same number and the loser would be rejected by the
    /// <c>(task_id, seq)</c> primary key — silently losing a row, because callers treat the
    /// append as best-effort and continue publishing. <c>AppendIngestStatusHistoryAsync</c> closes
    /// that window by deriving <c>seq</c> inside the INSERT statement itself; this test is
    /// what keeps a later refactor from splitting it back into a SELECT and an INSERT. Probed
    /// Red/Green: making that split fails here with <c>UNIQUE constraint failed</c>.
    /// State-based per Principle II — the assertions read the rows back out of the real
    /// SQLite file the production code wrote them to.
    /// </summary>
    [Fact]
    public async Task ConcurrentAppends_ForOneTask_AllPersist_WithDistinctSequentialSeq()
    {
        // Own database, no pipeline fixture: the contract under test is the repository's.
        // The writers run on dedicated threads (LongRunning), not pool threads — a writer
        // waiting out `busy_timeout` blocks where it stands, and borrowing pool threads for
        // that starves the liveness-sensitive tests running in parallel with this one.
        const int writers = 4;
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-history-concurrent-{Guid.NewGuid():N}");
        try
        {
            var repository = new OperationalStateRepository(Path.Combine(root, "operational-state.db"));
            await repository.InitializeAsync();

            var assigned = await Task.WhenAll(Enumerable.Range(0, writers).Select(i =>
                Task.Factory.StartNew(
                    () => repository.AppendIngestStatusHistoryAsync(
                        "task-concurrent", $"stage-{i}", DateTimeOffset.UtcNow, $"writer {i}"),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap()));

            var history = await repository.GetIngestStatusHistoryAsync("task-concurrent");

            Assert.Equal(writers, history.Count);
            Assert.Equal(Enumerable.Range(1, writers).Select(i => (long)i), history.Select(h => h.Seq));
            Assert.Equal(history.Select(h => h.Seq).Order(), assigned.Order());
            Assert.Equal(writers, history.Select(h => h.Status).Distinct().Count());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task History_ForAnUnknownTask_IsEmpty_NotAnError()
    {
        using var fixture = new IngestSubmissionPipelineFixture();

        Assert.Empty(await fixture.Repository.GetIngestStatusHistoryAsync("task-that-never-existed"));
    }
}
