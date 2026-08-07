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
/// <see cref="Source"/> is always one of <c>command-line</c>, <c>environment</c>, or
/// <c>config-file</c> (ADR-022: no code-default tier exists any more).
/// </summary>
public sealed record PathLocation(
    string Name,
    string ConfiguredValue,
    string ResolvedPath,
    PathLocationKind Kind,
    string Source);

/// <summary>
/// Everything one agent type needs to run — its subfolder, its worker DLL, and its
/// instruction surface (ADR-022, data-model.md §3). <see cref="DefaultUserPromptPath"/> is
/// non-null for Ingest only.
/// </summary>
public sealed record AgentRuntimePaths(
    string Dir,
    string WorkerPath,
    string InstructionsDir,
    string SystemPromptPath,
    string PolicyPath,
    string? DefaultUserPromptPath);

/// <summary>
/// The fully resolved and validated set of runtime locations (ADR-022), produced once at
/// startup by <see cref="GrimoirePathResolver"/> and registered as the only path source in
/// DI. Replaces the repo-root parameters of the former <c>ContentRootPaths</c> /
/// <c>RawStoragePaths</c> — those types now project from this record.
/// </summary>
public sealed record ResolvedGrimoirePaths(
    string DataDir,
    string WikiDir,
    string AgentDir,
    string RawOriginalsDir,
    string RawSourcesDir,
    string StateDbPath,
    string WriteLocksDir,
    string LintPidPath,
    string TasksDir,
    string ConversationsDir,
    string FindingsDir,
    string RemediationTasksDir,
    string IndexPath,
    string LogPath,
    string SecretsFilePath,
    AgentRuntimePaths Ingest,
    AgentRuntimePaths Query,
    AgentRuntimePaths Lint,
    IReadOnlyList<PathLocation> Locations)
{
    /// <summary>Per-task artifact path within <see cref="TasksDir"/> (mirrors IngestCliOptions.TaskArtifactPath).</summary>
    public string TaskArtifactPathFor(string taskId) => Path.Combine(TasksDir, $"{taskId}.md");

    /// <summary>Per-conversation Conversation Record path within <see cref="ConversationsDir"/> (ADR-014, 011-query-conversations data-model.md).</summary>
    public string ConversationRecordPathFor(string conversationId)
        => Path.Combine(ConversationsDir, $"{conversationId}.md");

    /// <summary>Per-run Findings Report path within <see cref="FindingsDir"/> (013-lint-agent data-model.md/contracts/findings-report-format.md).</summary>
    public string FindingsReportPathFor(string runId) => Path.Combine(FindingsDir, $"{runId}.md");

    /// <summary>Per-task Remediation Task Record path within <see cref="RemediationTasksDir"/> (015-lint-board-parity data-model.md, ADR-018/ADR-014).</summary>
    public string RemediationTaskRecordPathFor(string taskId)
        => Path.Combine(RemediationTasksDir, $"{taskId}.md");
}
