using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.IntegrationTests.Fakes;

/// <summary>
/// Builds a plausible <see cref="ResolvedGrimoirePaths"/> for tests that need one wired
/// to a temp root but don't exercise <see cref="GrimoirePathResolver"/> itself (they
/// construct the record directly, e.g. to drive a coordinator/watcher in isolation).
/// Every location nests under <paramref name="root"/>; agent worker paths are the literal
/// <c>"unused"</c> since these tests never spawn a real process. Callers needing one field
/// different from this default layout use the record's own <c>with</c> expression.
/// </summary>
internal static class TestResolvedGrimoirePathsFactory
{
    public static ResolvedGrimoirePaths Create(string root)
    {
        var wikiDir = Path.Combine(root, "wiki");
        var agentDir = Path.Combine(root, "agents");
        var memoryDir = Path.Combine(root, "memory");

        return new ResolvedGrimoirePaths(
            DataDir: root,
            WikiDir: wikiDir,
            AgentDir: agentDir,
            MemoryDir: memoryDir,
            RawOriginalsDir: Path.Combine(root, "raw", "originals"),
            RawSourcesDir: Path.Combine(root, "raw", "sources"),
            StateDbPath: Path.Combine(root, "operational-state.db"),
            WriteLocksDir: Path.Combine(root, "write-locks"),
            LintPidPath: Path.Combine(root, "lint.pid"),
            TasksDir: Path.Combine(memoryDir, "tasks"),
            ConversationsDir: Path.Combine(memoryDir, "conversations"),
            FindingsDir: Path.Combine(memoryDir, "findings"),
            RemediationTasksDir: Path.Combine(memoryDir, "remediation-tasks"),
            IndexPath: Path.Combine(wikiDir, "index.md"),
            LogPath: Path.Combine(wikiDir, "log.md"),
            SecretsFilePath: Path.Combine(root, ".env"),
            InstanceFoundationPromptPath: Path.Combine(root, "foundation-prompt.md"),
            Ingest: CreateAgentRuntimePaths(agentDir, "ingest", hasDefaultUserPrompt: true),
            Query: CreateAgentRuntimePaths(agentDir, "query", hasDefaultUserPrompt: false),
            Lint: CreateAgentRuntimePaths(agentDir, "lint", hasDefaultUserPrompt: false),
            Locations: []);
    }

    private static AgentRuntimePaths CreateAgentRuntimePaths(string agentDir, string agentId, bool hasDefaultUserPrompt)
    {
        var dir = Path.Combine(agentDir, agentId);
        var instructionsDir = Path.Combine(dir, "Instructions");

        return new AgentRuntimePaths(
            Dir: dir,
            WorkerPath: "unused",
            InstructionsDir: instructionsDir,
            FoundationPromptPath: Path.Combine(instructionsDir, "foundation-prompt.md"),
            SystemPromptPath: Path.Combine(instructionsDir, "system-prompt.md"),
            PolicyPath: Path.Combine(instructionsDir, "policy.json"),
            DefaultUserPromptPath: hasDefaultUserPrompt ? Path.Combine(instructionsDir, "default-user-prompt.md") : null);
    }
}
