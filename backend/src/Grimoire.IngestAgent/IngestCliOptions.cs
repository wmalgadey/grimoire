namespace Grimoire.IngestAgent;

public sealed record IngestCliOptions(
    string TaskId,
    string SourceRef,
    string SourceKind,
    string WikiRoot,
    string ContentRoot,
    string TasksDir,
    string IndexPath,
    string LogPath,
    string? PastedText,
    string SystemPromptPath,
    string DefaultUserPromptPath,
    string? UserPrompt,
    string PolicyPath,
    string WriteLocksDir,
    int HeartbeatSeconds = 10,
    // ADR-023 (022-align-wiki-structure, Phase 5): the ordered list of reserved
    // harness-surface names this run's operator has granted (empty = none granted).
    IReadOnlyList<string>? GrantedHarnessSurfaces = null)
{
    public string TaskArtifactPath => Path.Combine(TasksDir, $"{TaskId}.md");
}
