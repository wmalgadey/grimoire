namespace Grimoire.IngestAgent;

public sealed record AgentCliOptions(
    string TaskId,
    string SourceRef,
    string SourceKind,
    string WikiDir,
    string TasksDir,
    string IndexPath,
    string LogPath,
    string? PastedText)
{
    public string TaskArtifactPath => Path.Combine(TasksDir, $"{TaskId}.md");
}
