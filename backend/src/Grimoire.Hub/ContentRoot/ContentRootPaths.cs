namespace Grimoire.Hub.ContentRoot;

public sealed record ContentRootPaths(
    string Root,
    string PagesDir,
    string TasksDir,
    string IndexPath,
    string LogPath,
    string SystemPromptPath,
    string DefaultUserPromptPath,
    string PolicyPath)
{
    public static ContentRootPaths Resolve(string repoRoot, string contentRootDirName)
    {
        var root = Path.Combine(repoRoot, contentRootDirName);
        return new ContentRootPaths(
            Root: root,
            PagesDir: Path.Combine(root, "pages"),
            TasksDir: Path.Combine(root, "tasks"),
            IndexPath: Path.Combine(root, "index.md"),
            LogPath: Path.Combine(root, "log.md"),
            SystemPromptPath: Path.Combine(repoRoot, "agents", "ingest", "system-prompt.md"),
            DefaultUserPromptPath: Path.Combine(repoRoot, "agents", "ingest", "default-user-prompt.md"),
            PolicyPath: Path.Combine(repoRoot, "agents", "ingest", "policy.json"));
    }
}
