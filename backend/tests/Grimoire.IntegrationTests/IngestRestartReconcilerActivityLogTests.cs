using Grimoire.Hub.OperationalState;

namespace Grimoire.IntegrationTests;

/// <summary>
/// 025-agent-owned-log T016 — Feature-Scoped Invariant <b>FSI-2</b> (ADR-028): no harness
/// component writes to the resolved activity-log path, including the Hub's restart
/// reconciler.
///
/// Why this is behavioural rather than structural: Boundary Rule BR-1 covers the agent
/// assemblies with an IL scan, but <c>Grimoire.Hub</c> legitimately writes task artifacts,
/// operational state, conversation records, and findings by design, so an assembly-wide
/// "no filesystem writes" rule would be meaningless there. Per Constitution Principle III
/// a Feature-Scoped Invariant is verified by a classicist, state-based test of the real
/// observable behaviour — so this runs the real <see cref="RestartReconciler"/> against a
/// real temp content root and a real operational-state database, and asserts the activity
/// log's bytes are unchanged <em>while</em> the task artifact and status history are
/// updated. Asserting only the non-write would be satisfied by a reconciler that did
/// nothing at all.
/// </summary>
public class IngestRestartReconcilerActivityLogTests
{
    private const string SeededLog =
        "## [2026-08-01] ingest | created retrieval-patterns\n\n" +
        "Created [[concepts/retrieval-patterns]] from source \"notes.md\". Task: task-earlier.\n";

    [Fact]
    public async Task ReconcileRunningIngestTasks_LeavesActivityLogByteForByteUnchanged_WhileFailingTheTask()
    {
        var root = Path.Combine(Path.GetTempPath(), $"reconciler-log-{Guid.NewGuid():N}");
        var tasksDir = Path.Combine(root, "tasks");
        var wikiDir = Path.Combine(root, "wiki");
        Directory.CreateDirectory(tasksDir);
        Directory.CreateDirectory(wikiDir);
        try
        {
            // <WikiDir>/log.md is where GrimoirePathResolver puts the activity log, so that
            // is where this seeds it: the reconciler no longer takes a log path, and a
            // regression that reintroduced the write would resolve it exactly here.
            var logPath = Path.Combine(wikiDir, "log.md");
            await File.WriteAllTextAsync(logPath, SeededLog);
            var seededBytes = await File.ReadAllBytesAsync(logPath);

            const string taskId = "task-reconcile-001";
            var taskPath = Path.Combine(tasksDir, $"{taskId}.md");
            await File.WriteAllTextAsync(
                taskPath,
                "---\n" +
                $"task_id: {taskId}\n" +
                "status: running\n" +
                "completed_at: null\n" +
                "source_ref: \"raw/source.md\"\n" +
                "pages_touched: []\n" +
                "failure_reason: null\n" +
                "---\n\nRunning\n");

            var repository = new OperationalStateRepository(Path.Combine(root, "operational-state.db"));
            await repository.InitializeAsync();
            await repository.UpsertIngestTaskStateAsync(new IngestOperationalTaskState(taskId, "running", 100, DateTimeOffset.UtcNow));

            var reconciler = new RestartReconciler(repository);
            var count = await reconciler.ReconcileRunningIngestTasksAsync(tasksDir);

            Assert.Equal(1, count);

            // The wiki is untouched — byte-for-byte, not merely "still contains".
            Assert.Equal(seededBytes, await File.ReadAllBytesAsync(logPath));

            // ...and the failure is genuinely recorded where it belongs (SC-008): the task
            // artifact carries the outcome and its reason, discoverable without the wiki.
            var updatedTask = await File.ReadAllTextAsync(taskPath);
            Assert.Contains("status: failed", updatedTask, StringComparison.Ordinal);
            Assert.Contains(
                "failure_reason: \"Hub restarted while task was running.\"",
                updatedTask,
                StringComparison.Ordinal);

            // The stale running row is gone from operational state.
            Assert.DoesNotContain(await repository.GetIngestTaskStatesByStatusAsync("running"), x => x.TaskId == taskId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// FR-010's counterpart on the harness side: reconciliation must not <em>create</em>
    /// the activity log either. The first write is the agent's.
    /// </summary>
    [Fact]
    public async Task ReconcileRunningIngestTasks_DoesNotCreateTheActivityLog_WhenItDoesNotExist()
    {
        var root = Path.Combine(Path.GetTempPath(), $"reconciler-log-{Guid.NewGuid():N}");
        var tasksDir = Path.Combine(root, "tasks");
        var wikiDir = Path.Combine(root, "wiki");
        Directory.CreateDirectory(tasksDir);
        Directory.CreateDirectory(wikiDir);
        try
        {
            var logPath = Path.Combine(wikiDir, "log.md");

            const string taskId = "task-reconcile-002";
            await File.WriteAllTextAsync(
                Path.Combine(tasksDir, $"{taskId}.md"),
                "---\n" +
                $"task_id: {taskId}\n" +
                "status: running\n" +
                "completed_at: null\n" +
                "source_ref: \"raw/source.md\"\n" +
                "pages_touched: []\n" +
                "failure_reason: null\n" +
                "---\n\nRunning\n");

            var repository = new OperationalStateRepository(Path.Combine(root, "operational-state.db"));
            await repository.InitializeAsync();
            await repository.UpsertIngestTaskStateAsync(new IngestOperationalTaskState(taskId, "running", 100, DateTimeOffset.UtcNow));

            var reconciler = new RestartReconciler(repository);
            await reconciler.ReconcileRunningIngestTasksAsync(tasksDir);

            Assert.False(File.Exists(logPath));

            // Not just "no log.md": the wiki directory is untouched, so a reconciler that
            // wrote its reconciliation note anywhere else in the wiki fails here too.
            Assert.Empty(Directory.EnumerateFileSystemEntries(wikiDir));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
