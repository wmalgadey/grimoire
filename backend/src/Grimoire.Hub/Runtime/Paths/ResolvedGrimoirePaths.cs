namespace Grimoire.Hub.Runtime.Paths;

/// <summary>Whether a resolved location is validated as a required input or auto-created as writable data.</summary>
public enum PathLocationKind
{
    RequiredInput,
    WritableData,
}

/// <summary>
/// One resolved, reportable path location (data-model.md "PathLocation"). Carries the
/// vocabulary used by the startup report and by the <c>paths_*</c> structured log events.
/// </summary>
public sealed record PathLocation(
    string Name,
    string ConfiguredValue,
    string ResolvedPath,
    PathLocationKind Kind,
    string Source);

/// <summary>
/// The fully resolved and validated set of runtime locations (ADR-009), produced once at
/// startup by <see cref="GrimoirePathResolver"/> and registered as the only path source in
/// DI. Replaces the repo-root parameters of the former <c>ContentRootPaths</c> /
/// <c>RawStoragePaths</c> — those types now project from this record.
/// </summary>
public sealed record ResolvedGrimoirePaths(
    string BaseDir,
    string DataDir,
    string ContentRoot,
    string PagesDir,
    string TasksDir,
    string IndexPath,
    string LogPath,
    string RawOriginalsDir,
    string RawSourcesDir,
    string StateDbPath,
    string SecretsFilePath,
    string InstructionsDir,
    string SystemPromptPath,
    string DefaultUserPromptPath,
    string PolicyPath,
    string AgentWorkerPath,
    string QueryInstructionsDir,
    string QuerySystemPromptPath,
    string QueryPolicyPath,
    string QueryRunsDir,
    string QueryAgentWorkerPath,
    IReadOnlyList<PathLocation> Locations)
{
    /// <summary>Per-task artifact path within <see cref="TasksDir"/> (mirrors AgentCliOptions.TaskArtifactPath).</summary>
    public string TaskArtifactPathFor(string taskId) => Path.Combine(TasksDir, $"{taskId}.md");

    /// <summary>Per-turn Query Run Artifact path within <see cref="QueryRunsDir"/> (data-model.md, ADR-011 R7).</summary>
    public string QueryRunArtifactPathFor(string conversationId, string turnId)
        => Path.Combine(QueryRunsDir, conversationId, $"{turnId}.md");
}
