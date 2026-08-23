namespace Grimoire.LintAgent;

/// <summary>
/// The wiki index and activity log, derived from the run's wiki root.
///
/// <para>Unlike Ingest and Query, the Lint agent takes no <c>--index-path</c>/<c>--log-path</c>
/// switches: it never needed them, because under policy v1 both files were outside its write
/// scope entirely. FR-016a admits Lint to them, which makes FR-016b's requirement — that
/// admission must not relax the format rules governing them — immediately load-bearing:
/// <see cref="Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor"/> applies the catalog and
/// prepend checks only to the paths it is told about, so a Lint executor constructed without
/// them would write those two files unchecked.</para>
///
/// <para>Deriving rather than adding two CLI switches keeps the surface as it is and cannot
/// disagree with the Hub, which computes exactly these two paths the same way
/// (<c>GrimoirePathResolver</c>: fixed filenames under the resolved wiki directory, not
/// independently configurable — the same treatment ADR-020 gives the lint PID file).</para>
/// </summary>
internal static class LintPaths
{
    public static string IndexPath(string wikiRoot) => Path.Combine(wikiRoot, "index.md");

    public static string LogPath(string wikiRoot) => Path.Combine(wikiRoot, "log.md");
}
