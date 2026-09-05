using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.IngestDispatch;

namespace Grimoire.IntegrationTests;

public class IngestOperationalStateAndDispatchTests
{
    [Fact]
    public async Task OperationalStateRepository_Stores_And_Updates_Status()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-op-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var dbPath = Path.Combine(root, "operational-state.db");
        var repository = new OperationalStateRepository(dbPath);
        await repository.InitializeAsync();

        var taskId = "task-1";
        await repository.UpsertIngestTaskStateAsync(new IngestOperationalTaskState(taskId, "running", 1234, DateTimeOffset.UtcNow));
        await repository.UpsertIngestTaskStateAsync(new IngestOperationalTaskState(taskId, "completed", null, DateTimeOffset.UtcNow));

        var running = await repository.GetIngestTaskStatesByStatusAsync("running");
        var completed = await repository.GetIngestTaskStatesByStatusAsync("completed");

        Assert.Empty(running);
        Assert.Contains(completed, x => x.TaskId == taskId);
    }

    [Fact]
    public async Task Dispatcher_Spawns_Agent_And_Produces_Task_Artifact()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-dispatch-{Guid.NewGuid():N}");
        var tasksDir = Path.Combine(root, "tasks");
        var indexPath = Path.Combine(root, "index.md");
        var logPath = Path.Combine(root, "log.md");
        Directory.CreateDirectory(tasksDir);
        await File.WriteAllTextAsync(logPath, string.Empty);

        // Use a non-existent source path so the agent fails at source-read time,
        // before any LLM call — keeps the test hermetic (no API key or network needed).
        var sourcePath = Path.Combine(root, "nonexistent-source.md");

        var envPath = Path.Combine(root, ".env");
        var loader = new LocalSecretsLoader(envPath);

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
            SourceRef: sourcePath,
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

        // Agent should fail (exit 1) due to missing source, without making any LLM call.
        Assert.Equal(1, exitCode);
        var taskArtifactPath = Path.Combine(tasksDir, $"{taskId}.md");
        Assert.True(File.Exists(taskArtifactPath), "Task artifact must be written even on failure.");
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
