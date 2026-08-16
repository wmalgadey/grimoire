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
    // 023 T046 (FR-003): the Hub-resolved human-readable label (`--title`), carried verbatim
    // into every artifact write this process makes so it survives the agent taking the file
    // over from the Hub — the same pattern convert_steps already follows.
    string? Title = null)
{
    public string TaskArtifactPath => Path.Combine(TasksDir, $"{TaskId}.md");
}
