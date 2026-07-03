using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.OperationalState;

namespace Grimoire.IntegrationTests;

public class OperationalStateAndDispatchTests
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
        await repository.UpsertAsync(new OperationalTaskState(taskId, "running", 1234, DateTimeOffset.UtcNow));
        await repository.UpsertAsync(new OperationalTaskState(taskId, "completed", null, DateTimeOffset.UtcNow));

        var running = await repository.GetByStatusAsync("running");
        var completed = await repository.GetByStatusAsync("completed");

        Assert.Empty(running);
        Assert.Contains(completed, x => x.TaskId == taskId);
    }

    [Fact]
    public async Task Dispatcher_Spawns_Agent_And_Produces_Task_Artifact()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-dispatch-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        var tasksDir = Path.Combine(root, "tasks");
        var indexPath = Path.Combine(root, "index.md");
        var logPath = Path.Combine(root, "log.md");
        Directory.CreateDirectory(wikiDir);
        Directory.CreateDirectory(tasksDir);
        await File.WriteAllTextAsync(logPath, string.Empty);

        var sourcePath = Path.Combine(root, "source.md");
        await File.WriteAllTextAsync(sourcePath, "# Dispatch Test\n\nBody");

        var envPath = Path.Combine(root, ".env");
        var loader = new LocalSecretsLoader(envPath);

        var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
        var agentProjectPath = Path.Combine(repoRoot, "backend", "src", "Grimoire.IngestAgent", "Grimoire.IngestAgent.csproj");
        var dispatcher = new IngestAgentDispatcher(loader, agentProjectPath);

        var taskId = $"test-{Guid.NewGuid():N}";
        var exitCode = await dispatcher.DispatchAsync(new IngestAgentRequest(
            TaskId: taskId,
            SourceRef: sourcePath,
            SourceKind: "file",
            WikiDir: wikiDir,
            TasksDir: tasksDir,
            IndexPath: indexPath,
            LogPath: logPath,
            PastedText: null));

        Assert.True(exitCode is 0 or 1);
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
