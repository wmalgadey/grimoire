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
    string ConversationsDir,
    string QueryAgentWorkerPath,
    string WriteLocksDir,
    string FindingsDir,
    string LintInstructionsDir,
    string LintSystemPromptPath,
    string LintPolicyPath,
    string LintAgentWorkerPath,
    string RemediationTasksDir,
    string LintPidPath,
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
