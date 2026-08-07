using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.Hub.ContentRoot;

/// <summary>
/// Ingest-owned wiki-root and write-lock locations, as a flat projection of the
/// single-composition-point <see cref="ResolvedGrimoirePaths"/> (ADR-022). Kept as a
/// plain record (rather than folded into <see cref="ResolvedGrimoirePaths"/> itself) so
/// consumers that only need wiki-root paths, and hermetic tests, do not have to depend on
/// the full runtime-paths resolution/validation pipeline. Renamed from
/// <c>ContentRootPaths</c> (021-ingest-content-paths) to carry the Ingest token per rule
/// N1 (docs/conventions/agent-artifact-naming.md); no longer duplicates
/// <see cref="ResolvedGrimoirePaths.Ingest"/>'s instruction-file paths — consumers read
/// <c>SystemPromptPath</c>/<c>DefaultUserPromptPath</c>/<c>PolicyPath</c> from
/// <see cref="ResolvedGrimoirePaths.Ingest"/> directly.
/// </summary>
public sealed record IngestContentPaths(
    string Root,
    string TasksDir,
    string IndexPath,
    string LogPath,
    string WriteLocksDir)
{
    public static IngestContentPaths FromResolved(ResolvedGrimoirePaths resolved) =>
        new(
            Root: resolved.WikiDir,
            TasksDir: resolved.TasksDir,
            IndexPath: resolved.IndexPath,
            LogPath: resolved.LogPath,
            WriteLocksDir: resolved.WriteLocksDir);
}
