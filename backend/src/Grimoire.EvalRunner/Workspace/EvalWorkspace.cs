namespace Grimoire.EvalRunner.Workspace;

/// <summary>
/// Isolated per-sample workspace (spec 009 FR-015): a fresh copy of the scenario's wiki
/// fixture plus the agent instruction files under the OS temp directory. The agent
/// process operates only on these copies; the repository's `data/agents/` and wiki are
/// never touched by an eval run.
/// </summary>
public sealed class EvalWorkspace : IDisposable
{
    private EvalWorkspace(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string WikiRoot => Path.Combine(Root, "wiki");

    // A sibling of WikiRoot, not nested inside it (022-memory-directory-root, ADR-024):
    // mirrors production's MemoryDir/WikiDir split, so the eval workspace reproduces the
    // same tree the production agent sees rather than one where tasks/ is still reachable
    // while browsing the wiki.
    public string TasksDir => Path.Combine(Root, "tasks");

    public string IndexPath => Path.Combine(WikiRoot, "index.md");

    public string LogPath => Path.Combine(WikiRoot, "log.md");

    // 012-query-synthesis-writes (ADR-015): Ingest's GuardedToolExecutor now requires a
    // write-coordination lock directory (T041 made `--write-locks-dir` a required CLI
    // argument on Grimoire.IngestAgent) — mirrors QueryEvalSandbox.WriteLocksDir, one
    // per-sample directory so concurrent samples never share lock files.
    public string WriteLocksDir => Path.Combine(Root, "write-locks");

    public string AgentDir => Path.Combine(Root, "agents", "ingest");

    public string SystemPromptPath => Path.Combine(AgentDir, "system-prompt.md");

    public string DefaultUserPromptPath => Path.Combine(AgentDir, "default-user-prompt.md");

    public string PolicyPath => Path.Combine(AgentDir, "policy.json");

    /// <summary>
    /// Creates the workspace: fixture wiki + instruction directory copies, optional
    /// system-prompt mutation (instruction-change scenario), plus the tasks dir
    /// the agent CLI contract expects.
    /// </summary>
    public static async Task<EvalWorkspace> CreateAsync(
        string wikiFixtureRoot,
        string agentInstructionsDir,
        string taskId,
        string? systemPromptAppendix = null,
        CancellationToken cancellationToken = default)
    {
        // The task id may repeat across capture/replay of the same sample (it is part of
        // the recorded conversation), so the directory gets its own unique suffix.
        var root = Path.Combine(Path.GetTempPath(), "grimoire-eval-runner", $"{taskId}-{Guid.NewGuid():N}");
        var workspace = new EvalWorkspace(root);

        await Task.WhenAll(
            CopyDirectoryAsync(wikiFixtureRoot, workspace.WikiRoot, cancellationToken),
            CopyDirectoryAsync(agentInstructionsDir, workspace.AgentDir, cancellationToken));

        Directory.CreateDirectory(workspace.TasksDir);
        Directory.CreateDirectory(workspace.WriteLocksDir);

        if (!string.IsNullOrEmpty(systemPromptAppendix))
        {
            var baseline = await File.ReadAllTextAsync(workspace.SystemPromptPath, cancellationToken);
            if (!baseline.Contains(systemPromptAppendix, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(
                    workspace.SystemPromptPath, baseline.TrimEnd() + "\n\n" + systemPromptAppendix + "\n", cancellationToken);
            }
        }

        return workspace;
    }

    public IReadOnlyList<string> PageFiles()
        => !Directory.Exists(WikiRoot)
            ? []
            : Directory.GetFiles(WikiRoot, "*.md", SearchOption.AllDirectories)
                .Where(path => !string.Equals(path, IndexPath, StringComparison.Ordinal)
                    && !string.Equals(path, LogPath, StringComparison.Ordinal))
                .OrderBy(static p => p, StringComparer.Ordinal)
                .ToArray();

    public string IndexContent() => File.Exists(IndexPath) ? File.ReadAllText(IndexPath) : string.Empty;

    public string LogContent() => File.Exists(LogPath) ? File.ReadAllText(LogPath) : string.Empty;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; OS temp reclamation handles leftovers.
        }
    }

    // 019-fast-test-tier (US4, FR-013, research.md R7): parallelizes per-sample workspace
    // setup — the source directory (a small, fixed fixture) is shared and read-only across
    // samples, so concurrent copies from it introduce no new isolation risk; each sample's
    // destination is already unique per call. File copies within one directory run
    // concurrently via Parallel.ForEachAsync; subdirectories recurse the same way via
    // Task.WhenAll, so the whole tree copies in parallel, not just one level.
    private static async Task CopyDirectoryAsync(string sourceDir, string destinationDir, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDir);

        var copyFiles = Parallel.ForEachAsync(
            Directory.GetFiles(sourceDir),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            (file, _) =>
            {
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
                return ValueTask.CompletedTask;
            });

        var copySubdirectories = Task.WhenAll(Directory.GetDirectories(sourceDir)
            .Select(directory => CopyDirectoryAsync(directory, Path.Combine(destinationDir, Path.GetFileName(directory)), cancellationToken)));

        await Task.WhenAll(copyFiles, copySubdirectories);
    }
}
