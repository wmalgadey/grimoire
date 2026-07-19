using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.Hub.ContentRoot;

/// <summary>
/// Wiki content root and agent-instruction locations, as a flat projection of the
/// single-composition-point <see cref="ResolvedGrimoirePaths"/> (ADR-009). Kept as a
/// plain record (rather than folded into <see cref="ResolvedGrimoirePaths"/> itself) so
/// consumers that only need content-root paths, and hermetic tests, do not have to
/// depend on the full runtime-paths resolution/validation pipeline.
/// </summary>
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
    public static ContentRootPaths FromResolved(ResolvedGrimoirePaths resolved) =>
        new(
            Root: resolved.ContentRoot,
            PagesDir: resolved.PagesDir,
            TasksDir: resolved.TasksDir,
            IndexPath: resolved.IndexPath,
            LogPath: resolved.LogPath,
            SystemPromptPath: resolved.SystemPromptPath,
            DefaultUserPromptPath: resolved.DefaultUserPromptPath,
            PolicyPath: resolved.PolicyPath);
}
