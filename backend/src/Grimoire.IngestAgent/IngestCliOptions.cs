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
    int HeartbeatSeconds = 10)
{
    public string TaskArtifactPath => Path.Combine(TasksDir, $"{TaskId}.md");
}
