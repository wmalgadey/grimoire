using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.IngestDispatch;

namespace Grimoire.IntegrationTests;

public class IngestFailureAndReconciliationTests
{
    [Fact]
    public async Task FailurePath_LeavesWikiUntouched_AndMarksTaskFailed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-fail-{Guid.NewGuid():N}");
        var tasksDir = Path.Combine(root, "tasks");
        var indexPath = Path.Combine(root, "index.md");
        var logPath = Path.Combine(root, "log.md");
        Directory.CreateDirectory(tasksDir);
        await File.WriteAllTextAsync(logPath, string.Empty);

        var loader = new LocalSecretsLoader(Path.Combine(root, ".env"));
        // ADR-022: the hub launches only pre-built worker DLLs (one launch mode, rule R4)
        // — never a .csproj. Deliberately NOT AppContext.BaseDirectory: ProjectReference
        // copy-local only copies what THIS test project's own dependency closure needs,
        // which can omit assemblies the agent needs but this test host takes from
        // elsewhere (research.md R5's documented failure mode) — the agent's own build
        // output under .grimoire/agents/ (produced by PublishAgentRuntime, which copies
        // the agent's ENTIRE $(OutDir)) is the only copy guaranteed complete.
        var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
        var agentDir = Path.Combine(repoRoot, ".grimoire", "agents");
        var agentWorkerPath = Path.Combine(agentDir, "ingest", "Grimoire.IngestAgent.dll");
        var queryAgentWorkerPath = Path.Combine(agentDir, "query", "Grimoire.QueryAgent.dll");
        var lintAgentWorkerPath = Path.Combine(agentDir, "lint", "Grimoire.LintAgent.dll");
        var processHost = new AgentProcessHost(loader, agentWorkerPath, queryAgentWorkerPath, lintAgentWorkerPath);

        var taskId = $"test-{Guid.NewGuid():N}";
        var instructionsDir = Path.Combine(repoRoot, "backend", "src", "Grimoire.IngestAgent", "Instructions");
        var exitCode = await processHost.RunToExitAsync(new IngestAgentRequest(
            TaskId: taskId,
            SourceRef: Path.Combine(root, "missing-source.md"),
            SourceKind: "file",
            WikiRoot: root,
            ContentRoot: root,
            TasksDir: tasksDir,
            IndexPath: indexPath,
            LogPath: logPath,
            PastedText: null,
            SystemPromptPath: Path.Combine(instructionsDir, "system-prompt.md"),
            DefaultUserPromptPath: Path.Combine(instructionsDir, "default-user-prompt.md"),
            PolicyPath: Path.Combine(instructionsDir, "policy.json"),
            WriteLocksDir: Path.Combine(root, "write-locks")));

        Assert.Equal(1, exitCode);
        // No new content-root entries (e.g. a topical category folder or article) were
        // created on the failure path — only the pre-existing tasksDir/log.md remain.
        var contentEntries = Directory.GetFileSystemEntries(root)
            .Select(Path.GetFileName)
            .Where(name => name is not ("tasks" or "log.md"))
            .ToArray();
        Assert.Empty(contentEntries);
        Assert.False(File.Exists(indexPath));

        var taskArtifact = await File.ReadAllTextAsync(Path.Combine(tasksDir, $"{taskId}.md"));
        Assert.Contains("status: failed", taskArtifact);
        Assert.Contains("failure_reason:", taskArtifact);

        // 025-agent-owned-log (FR-012, SC-008): removing the harness fallback entry must
        // cost no diagnostic capability, so the failed run stays fully accounted for
        // *without consulting the wiki*. Everything an operator needs is in the task
        // artifact: the outcome, the stage it failed at, and the correlation reference.
        Assert.Contains($"task_id: {taskId}", taskArtifact, StringComparison.Ordinal);
        Assert.Contains("completed_at:", taskArtifact, StringComparison.Ordinal);
        Assert.Contains("source_ref:", taskArtifact, StringComparison.Ordinal);

        // ...and the activity log is exactly as the test seeded it — empty. The failure is
        // discoverable above; none of it was written here (FR-001, FR-002, SC-002).
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(logPath));
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
        var count = await reconciler.ReconcileRunningTasksAsync(tasksDir);

        Assert.Equal(1, count);

        var updatedTask = await File.ReadAllTextAsync(taskPath);
        Assert.Contains("status: failed", updatedTask);
        Assert.Contains("failure_reason: \"Hub restarted while task was running.\"", updatedTask);

        // 025-agent-owned-log (ADR-028, FR-001, SC-002): reconciliation writes nothing to
        // the wiki. It used to append its own "reconciled on startup" entry here; the
        // activity log is agent-authored wiki content, so the file is left exactly as the
        // test seeded it.
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(logPath));

        // After successful reconciliation the stale operational-state row is deleted;
        // the task artifact is the durable record (ADR-003).
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
