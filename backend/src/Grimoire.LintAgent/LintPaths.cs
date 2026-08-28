using System.Linq;

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

    /// <summary>
    /// 028-lint-at-scale (US2, FR-003/FR-004): the `WikiCoverage.PagesTotal` snapshot — a
    /// recursive count of markdown pages under the wiki root, taken at run start (before
    /// the agent loop runs, so a run's own writes cannot change the denominator it is
    /// scored against). Missing/unreadable subdirectories are skipped rather than failing
    /// the run, matching <c>GuardedToolExecutor.EnumerateSearchCandidates</c>'s tolerance.
    /// </summary>
    public static int CountMarkdownPages(string wikiRoot)
    {
        if (!Directory.Exists(wikiRoot))
        {
            return 0;
        }

        try
        {
            return Directory.EnumerateFiles(wikiRoot, "*.md", SearchOption.AllDirectories).Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
