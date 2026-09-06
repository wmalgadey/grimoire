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
/// non-null for Ingest only. <see cref="FoundationPromptPath"/> (ADR-053,
/// 029-shared-foundation-prompt) is this agent's own build-distributed copy of the shared
/// foundation document — the fallback an instance uses when it has set no document of its
/// own; see <see cref="ResolvedGrimoirePaths.ResolveEffectiveFoundationPrompt"/> for which
/// one actually wins for a given dispatch.
/// </summary>
public sealed record AgentRuntimePaths(
    string Dir,
    string WorkerPath,
    string InstructionsDir,
    string FoundationPromptPath,
    string SystemPromptPath,
    string PolicyPath,
    string? DefaultUserPromptPath);

/// <summary>
/// The foundation document a dispatch actually operates under, and which of the two
/// possible sources it came from (029-shared-foundation-prompt,
/// contracts/foundation-document.md). <see cref="Source"/> is exactly <c>"default"</c> or
/// <c>"instance"</c> — the vocabulary the <c>wiki_identity_foundation_resolved</c> log
/// event and the <c>wiki.identity.foundation_resolved_total</c> metric report.
/// <see cref="Sha256"/> is the document's content hash, computed at resolution time — the
/// version identity this dispatch's <c>wiki_identity_foundation_resolved</c> event and
/// per-run instruction record both carry (data-model.md §4/§5).
/// </summary>
public sealed record EffectiveFoundationPrompt(string Path, string Source, string Sha256);

/// <summary>
/// The fully resolved and validated set of runtime locations (ADR-022), produced once at
/// startup by <see cref="GrimoirePathResolver"/> and registered as the only path source in
/// DI. Replaces the repo-root parameters of the former <c>ContentRootPaths</c> /
/// <c>IngestRawStoragePaths</c> — those types now project from this record.
/// </summary>
public sealed record ResolvedGrimoirePaths(
    string DataDir,
    string WikiDir,
    string AgentDir,
    string MemoryDir,
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
    string InstanceFoundationPromptPath,
    AgentRuntimePaths Ingest,
    AgentRuntimePaths Query,
    AgentRuntimePaths Lint,
    IReadOnlyList<PathLocation> Locations)
{
    /// <summary>Per-task artifact path within <see cref="TasksDir"/> (mirrors IngestCliOptions.TaskArtifactPath).</summary>
    public string TaskArtifactPathFor(string taskId) => Path.Combine(TasksDir, $"{taskId}.md");

    /// <summary>
    /// The effective foundation document for one agent's dispatch (029-shared-foundation-prompt,
    /// contracts/foundation-document.md): <see cref="InstanceFoundationPromptPath"/> when it
    /// exists, wins for every agent identically; otherwise <paramref name="agentPaths"/>'s own
    /// build-distributed default. Deliberately re-checked with <see cref="File.Exists(string?)"/>
    /// on every call rather than cached anywhere on this record — an instance document the
    /// wizard writes must take effect on the very next dispatch, with no Hub restart (FR-002,
    /// FR-008, FR-017).
    /// </summary>
    public EffectiveFoundationPrompt ResolveEffectiveFoundationPrompt(AgentRuntimePaths agentPaths)
    {
        var (path, source) = File.Exists(InstanceFoundationPromptPath)
            ? (InstanceFoundationPromptPath, "instance")
            : (agentPaths.FoundationPromptPath, "default");
        return new EffectiveFoundationPrompt(path, source, ComputeSha256(path));
    }

    private static string ComputeSha256(string path)
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Per-conversation Conversation Record path within <see cref="ConversationsDir"/> (ADR-014, 011-query-conversations data-model.md).</summary>
    public string ConversationRecordPathFor(string conversationId)
        => Path.Combine(ConversationsDir, $"{conversationId}.md");

    /// <summary>Per-run Findings Report path within <see cref="FindingsDir"/> (013-lint-agent data-model.md/contracts/findings-report-format.md).</summary>
    public string FindingsReportPathFor(string runId) => Path.Combine(FindingsDir, $"{runId}.md");

    /// <summary>Per-task Remediation Task Record path within <see cref="RemediationTasksDir"/> (015-lint-board-parity data-model.md, ADR-018/ADR-014).</summary>
    public string RemediationTaskRecordPathFor(string taskId)
        => Path.Combine(RemediationTasksDir, $"{taskId}.md");
}
