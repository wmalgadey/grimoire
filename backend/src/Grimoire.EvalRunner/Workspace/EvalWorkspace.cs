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

    public string PagesDir => Path.Combine(WikiRoot, "pages");

    public string TasksDir => Path.Combine(WikiRoot, "tasks");

    public string IndexPath => Path.Combine(WikiRoot, "index.md");

    public string LogPath => Path.Combine(WikiRoot, "log.md");

    public string AgentDir => Path.Combine(Root, "agents", "ingest");

    public string SystemPromptPath => Path.Combine(AgentDir, "system-prompt.md");

    public string DefaultUserPromptPath => Path.Combine(AgentDir, "default-user-prompt.md");

    public string PolicyPath => Path.Combine(AgentDir, "policy.json");

    /// <summary>
    /// Creates the workspace: fixture wiki + instruction directory copies, optional
    /// system-prompt mutation (instruction-change scenario), plus the pages/tasks dirs
    /// the agent CLI contract expects.
    /// </summary>
    public static EvalWorkspace Create(
        string wikiFixtureRoot,
        string agentInstructionsDir,
        string taskId,
        string? systemPromptAppendix = null)
    {
        // The task id may repeat across capture/replay of the same sample (it is part of
        // the recorded conversation), so the directory gets its own unique suffix.
        var root = Path.Combine(Path.GetTempPath(), "grimoire-eval-runner", $"{taskId}-{Guid.NewGuid():N}");
        var workspace = new EvalWorkspace(root);

        CopyDirectory(wikiFixtureRoot, workspace.WikiRoot);
        CopyDirectory(agentInstructionsDir, workspace.AgentDir);

        Directory.CreateDirectory(workspace.PagesDir);
        Directory.CreateDirectory(workspace.TasksDir);

        if (!string.IsNullOrEmpty(systemPromptAppendix))
        {
            var baseline = File.ReadAllText(workspace.SystemPromptPath);
            if (!baseline.Contains(systemPromptAppendix, StringComparison.Ordinal))
            {
                File.WriteAllText(workspace.SystemPromptPath, baseline.TrimEnd() + "\n\n" + systemPromptAppendix + "\n");
            }
        }

        return workspace;
    }

    public IReadOnlyList<string> PageFiles()
        => !Directory.Exists(PagesDir)
            ? []
            : Directory.GetFiles(PagesDir, "*.md", SearchOption.AllDirectories)
                .OrderBy(static p => p, StringComparer.Ordinal)
                .ToArray();

    public string IndexContent() => File.Exists(IndexPath) ? File.ReadAllText(IndexPath) : string.Empty;

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

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }
    }
}
