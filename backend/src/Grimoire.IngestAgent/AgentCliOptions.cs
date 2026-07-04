namespace Grimoire.IngestAgent;

public sealed record AgentCliOptions(
    string TaskId,
    string SourceRef,
    string SourceKind,
    string PagesDir,
    string TasksDir,
    string IndexPath,
    string LogPath,
    string GuardrailPolicyPath,
    string InstructionsRoot,
    IReadOnlyList<string> SkillPaths,
    string? SkillName,
    bool DryRun,
    string? PastedText)
{
    public string TaskArtifactPath => Path.Combine(TasksDir, $"{TaskId}.md");
}
