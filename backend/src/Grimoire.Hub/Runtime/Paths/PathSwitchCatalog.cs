namespace Grimoire.Hub.Runtime.Paths;

/// <summary>
/// A single ADR-022 command-line switch — pairs the CLI switch name with its
/// configuration key (for AddCommandLine) and its human-readable description (for
/// --help output), so all three can never independently drift.
/// </summary>
internal sealed record PathSwitch(string Name, string ConfigKey, string Description);

/// <summary>
/// Single source of truth for the ADR-022 switch vocabulary, amended by ADR-024 rule M1
/// — structurally capped at exactly these four entries by
/// <c>Grimoire.ArchTests.DirectorySwitchSurfaceRuleTests</c>, so the surface cannot
/// regrow the way it did under ADR-009's uncapped "single source of truth" rule. Both
/// Program.cs's AddCommandLine wiring and its --help usage text, as well as
/// HubHelpUsageTests' parity assertions, derive from this one list — internal visibility
/// plus [assembly: InternalsVisibleTo("Grimoire.IntegrationTests")] (AssemblyInfo.cs)
/// lets the test reference it directly instead of maintaining a second hardcoded copy.
///
/// <see cref="PathSwitch.ConfigKey"/> values are nested (<c>Grimoire:Paths:Data:Dir</c>,
/// not <c>Grimoire:Paths:DataDir</c>) per ADR-024's configuration-file regrouping
/// (research R8) — the switch <em>names</em> and the <c>PathLocation</c> names are
/// unaffected by that rename.
/// </summary>
internal static class PathSwitchCatalog
{
    public static readonly IReadOnlyList<PathSwitch> All =
    [
        new("--data-dir", "Grimoire:Paths:Data:Dir", "Root for harness runtime state (raw intake, state DB, write-locks)."),
        new("--agent-dir", "Grimoire:Paths:Agent:Dir", "Directory holding the complete agent runtime — worker binaries, dependency assemblies and instruction files — in per-agent-type subfolders. Produced by the agent build."),
        new("--wiki-dir", "Grimoire:Paths:Wiki:Dir", "Root for the wiki content itself — index.md, log.md, and topical article folders."),
        new("--memory-dir", "Grimoire:Paths:Memory:Dir", "Root for agent process bookkeeping — task artifacts, conversation records, lint findings reports, remediation task records."),
    ];
}
