namespace Grimoire.EvalRunner.Workspace;

/// <summary>
/// Per-sample wiki sandbox for Query eval runs (012-query-synthesis-writes, ADR-015).
/// Before this feature, <see cref="Capture.QueryCapturePipeline"/>/<see cref="Replay.QueryReplayPipeline"/>
/// pointed every sample directly at the checked-in fixture wiki
/// (<see cref="EvalPaths.FixtureWikiRoot"/>) — safe only because Query was strictly
/// read-only (spec 008), so nothing could ever mutate the fixture. Now that Query can
/// create pages, running more than one sample against the same on-disk fixture would
/// have the second sample's create-only write collide with a page the first sample
/// already created (or leave the checked-in fixture directory permanently dirty). This
/// mirrors the isolation guarantee <see cref="EvalWorkspace"/> already provides for
/// Ingest (spec 009 FR-015): each sample gets its own disposable copy of the wiki, and
/// its own write-coordination lock directory (ADR-015's <c>--write-locks-dir</c>).
/// </summary>
public sealed class QueryEvalSandbox : IDisposable
{
    private QueryEvalSandbox(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string WikiRoot => Path.Combine(Root, "wiki");

    public string WriteLocksDir => Path.Combine(Root, "write-locks");

    /// <summary>Creates a fresh copy of <paramref name="fixtureWikiRoot"/> under the OS temp directory.</summary>
    public static QueryEvalSandbox Create(string fixtureWikiRoot, string label)
    {
        var root = Path.Combine(Path.GetTempPath(), "grimoire-eval-runner", $"{label}-{Guid.NewGuid():N}");
        var sandbox = new QueryEvalSandbox(root);
        CopyDirectory(fixtureWikiRoot, sandbox.WikiRoot);
        Directory.CreateDirectory(sandbox.WriteLocksDir);
        return sandbox;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; OS temp reclamation handles leftovers.
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }
    }
}
