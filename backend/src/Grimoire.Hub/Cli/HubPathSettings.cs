using System.ComponentModel;
using Grimoire.Hub.Runtime.Paths;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Shared base settings declaring every ADR-022 path switch, inherited by every command's
/// own settings class so all commands accept the same server-location switches the web
/// host does (018-hub-cli-commands: commands now resolve the data directory in-process).
///
/// This is SYNTAX-ONLY on Spectre's side: these properties exist so Spectre's parser
/// accepts the switches and its generated help lists them per-command, but they are
/// never read directly by command code. The values that actually govern path resolution
/// still flow through the pre-existing configuration composition
/// (<c>builder.Configuration.AddCommandLine(args, PathConfigurationSwitchMappingsFactory())</c>
/// in <see cref="HubHostComposition"/>, mirroring <c>Program.cs</c>'s web-host path),
/// preserving ADR-022's CLI &gt; env &gt; appsettings.json precedence chain. Binding this
/// class's properties instead would create a second, competing source of truth for path
/// values — deliberately avoided.
///
/// One property per <see cref="PathSwitchCatalog.All"/> entry — structurally capped at
/// exactly three by <c>Grimoire.ArchTests.DirectorySwitchSurfaceRuleTests</c> (rule R1);
/// a parity test (<c>HubHelpUsageTests</c>) asserts the 1:1 mapping so the two can never
/// independently drift. The <see cref="DescriptionAttribute"/> text below mirrors
/// <see cref="PathSwitchCatalog.All"/>'s descriptions so `&lt;command&gt; --help` shows
/// the same wording as the root help's "Server options:" section.
/// </summary>
public class HubPathSettings : CommandSettings
{
    [CommandOption("--data-dir <PATH>")]
    [Description("Root for harness runtime state (raw intake, state DB, write-locks) and, by default, the agent directory.")]
    public string? DataDir { get; set; }

    [CommandOption("--agent-dir <PATH>")]
    [Description("Directory holding the complete agent runtime — worker binaries, dependency assemblies and instruction files — in per-agent-type subfolders. Produced by the agent build.")]
    public string? AgentDir { get; set; }

    [CommandOption("--wiki-dir <PATH>")]
    [Description("Root for all agent-produced results (wiki pages, index.md, log.md, tasks, conversations, findings, remediation tasks).")]
    public string? WikiDir { get; set; }
}
