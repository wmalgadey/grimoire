namespace Grimoire.Hub.Runtime.Paths;

/// <summary>
/// A single ADR-022 command-line switch — pairs the CLI switch name with its
/// configuration key (for AddCommandLine) and its human-readable description (for
/// --help output), so all three can never independently drift.
/// </summary>
internal sealed record PathSwitch(string Name, string ConfigKey, string Description);

/// <summary>
/// Single source of truth for the ADR-022 switch vocabulary — structurally capped at
/// exactly these three entries by <c>Grimoire.ArchTests.DirectorySwitchSurfaceRuleTests</c>
/// (rule R1), so the surface cannot regrow the way it did under ADR-009's uncapped
/// "single source of truth" rule. Both Program.cs's AddCommandLine wiring and its --help
/// usage text, as well as HubHelpUsageTests' parity assertions, derive from this one list
/// — internal visibility plus [assembly: InternalsVisibleTo("Grimoire.IntegrationTests")]
/// (AssemblyInfo.cs) lets the test reference it directly instead of maintaining a second
/// hardcoded copy.
/// </summary>
internal static class PathSwitchCatalog
{
    public static readonly IReadOnlyList<PathSwitch> All =
    [
        new("--data-dir", "Grimoire:Paths:DataDir", "Root for harness runtime state (raw intake, state DB, write-locks) and, by default, the agent directory."),
        new("--agent-dir", "Grimoire:Paths:AgentDir", "Directory holding the complete agent runtime — worker binaries, dependency assemblies and instruction files — in per-agent-type subfolders. Produced by the agent build."),
        new("--wiki-dir", "Grimoire:Paths:WikiDir", "Root for all agent-produced results (wiki pages, index.md, log.md, tasks, conversations, findings, remediation tasks)."),
    ];
}
