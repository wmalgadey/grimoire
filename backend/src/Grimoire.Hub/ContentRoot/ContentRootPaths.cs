using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.Hub.ContentRoot;

/// <summary>
/// Wiki root and Ingest agent-instruction locations, as a flat projection of the
/// single-composition-point <see cref="ResolvedGrimoirePaths"/> (ADR-022). Kept as a
/// plain record (rather than folded into <see cref="ResolvedGrimoirePaths"/> itself) so
/// consumers that only need wiki-root paths, and hermetic tests, do not have to depend on
/// the full runtime-paths resolution/validation pipeline. Retains its pre-020 name
/// (research.md R9): internal projection type, no operator-facing surface.
/// </summary>
public sealed record ContentRootPaths(
    string Root,
    string TasksDir,
    string IndexPath,
    string LogPath,
    string SystemPromptPath,
    string DefaultUserPromptPath,
    string PolicyPath,
    string WriteLocksDir)
{
    public static ContentRootPaths FromResolved(ResolvedGrimoirePaths resolved) =>
        new(
            Root: resolved.WikiDir,
            TasksDir: resolved.TasksDir,
            IndexPath: resolved.IndexPath,
            LogPath: resolved.LogPath,
            SystemPromptPath: resolved.Ingest.SystemPromptPath,
            DefaultUserPromptPath: resolved.Ingest.DefaultUserPromptPath!,
            PolicyPath: resolved.Ingest.PolicyPath,
            WriteLocksDir: resolved.WriteLocksDir);
}
