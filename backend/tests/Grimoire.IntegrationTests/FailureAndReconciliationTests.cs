using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.OperationalState;

namespace Grimoire.IntegrationTests;

public class FailureAndReconciliationTests
{
    [Fact]
    public async Task FailurePath_LeavesWikiUntouched_AndMarksTaskFailed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-fail-{Guid.NewGuid():N}");
        var pagesDir = Path.Combine(root, "pages");
        var tasksDir = Path.Combine(root, "tasks");
        var indexPath = Path.Combine(root, "index.md");
        var logPath = Path.Combine(root, "log.md");
        Directory.CreateDirectory(pagesDir);
        Directory.CreateDirectory(tasksDir);
        await File.WriteAllTextAsync(logPath, string.Empty);

        var loader = new LocalSecretsLoader(Path.Combine(root, ".env"));
        var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
        var agentProjectPath = Path.Combine(repoRoot, "backend", "src", "Grimoire.IngestAgent", "Grimoire.IngestAgent.csproj");
        var processHost = new AgentProcessHost(loader, agentProjectPath);

        var taskId = $"test-{Guid.NewGuid():N}";
        var repoRootForPaths = FindRepoRoot(Directory.GetCurrentDirectory());
        var exitCode = await processHost.RunToExitAsync(new IngestAgentRequest(
            TaskId: taskId,
            SourceRef: Path.Combine(root, "missing-source.md"),
            SourceKind: "file",
            WikiRoot: root,
            PagesDir: pagesDir,
            TasksDir: tasksDir,
            IndexPath: indexPath,
            LogPath: logPath,
            PastedText: null,
            SystemPromptPath: Path.Combine(repoRootForPaths, "agents", "ingest", "system-prompt.md"),
            DefaultUserPromptPath: Path.Combine(repoRootForPaths, "agents", "ingest", "default-user-prompt.md"),
            PolicyPath: Path.Combine(repoRootForPaths, "agents", "ingest", "policy.json")));

        Assert.Equal(1, exitCode);
        Assert.Empty(Directory.GetFiles(pagesDir));
        Assert.False(File.Exists(indexPath));

        var taskArtifact = await File.ReadAllTextAsync(Path.Combine(tasksDir, $"{taskId}.md"));
        Assert.Contains("status: failed", taskArtifact);
        Assert.Contains("failure_reason:", taskArtifact);
    }

    [Fact]
    public async Task RestartReconciliation_UpdatesTaskArtifactAndOperationalState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-reconcile-{Guid.NewGuid():N}");
        var tasksDir = Path.Combine(root, "tasks");
        var logPath = Path.Combine(root, "log.md");
        Directory.CreateDirectory(tasksDir);
        await File.WriteAllTextAsync(logPath, string.Empty);

        var taskId = $"task-{Guid.NewGuid():N}";
        var taskPath = Path.Combine(tasksDir, $"{taskId}.md");
        await File.WriteAllTextAsync(taskPath,
            "---\n" +
            $"task_id: {taskId}\n" +
            "type: ingest\n" +
            "status: running\n" +
            "agent: ingest\n" +
            "started_at: 2026-07-03T00:00:00Z\n" +
            "completed_at: null\n" +
            "source_ref: \"raw/source.md\"\n" +
            "pages_touched: []\n" +
            "failure_reason: null\n" +
            "---\n\nRunning\n");

        var dbPath = Path.Combine(root, "operational-state.db");
        var repository = new OperationalStateRepository(dbPath);
        await repository.InitializeAsync();
        await repository.UpsertAsync(new OperationalTaskState(taskId, "running", 100, DateTimeOffset.UtcNow));

        var reconciler = new RestartReconciler(repository);
        var count = await reconciler.ReconcileRunningTasksAsync(tasksDir, logPath);

        Assert.Equal(1, count);

        var updatedTask = await File.ReadAllTextAsync(taskPath);
        Assert.Contains("status: failed", updatedTask);
        Assert.Contains("failure_reason: \"Hub restarted while task was running.\"", updatedTask);

        var logText = await File.ReadAllTextAsync(logPath);
        Assert.Contains("reconciled on startup", logText);

        // After successful reconciliation the stale operational-state row is deleted;
        // the task artifact and log.md are the durable record (ADR-003).
        var running = await repository.GetByStatusAsync("running");
        Assert.DoesNotContain(running, x => x.TaskId == taskId);
        var failed = await repository.GetByStatusAsync("failed");
        Assert.DoesNotContain(failed, x => x.TaskId == taskId);
    }

    private static string FindRepoRoot(string start)
    {
        var current = Path.GetFullPath(start);
        while (true)
        {
            if (Directory.Exists(Path.Combine(current, ".specify")) && Directory.Exists(Path.Combine(current, "specs")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                throw new InvalidOperationException("Could not find repository root.");
            }

            current = parent.FullName;
        }
    }
}
