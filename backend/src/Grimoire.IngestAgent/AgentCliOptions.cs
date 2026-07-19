namespace Grimoire.IngestAgent;

public sealed record AgentCliOptions(
    string TaskId,
    string SourceRef,
    string SourceKind,
    string WikiRoot,
    string PagesDir,
    string TasksDir,
    string IndexPath,
    string LogPath,
    string? PastedText,
    string SystemPromptPath,
    string DefaultUserPromptPath,
    string? UserPrompt,
    string PolicyPath,
    int HeartbeatSeconds = 10)
{
    public string TaskArtifactPath => Path.Combine(TasksDir, $"{TaskId}.md");
}
