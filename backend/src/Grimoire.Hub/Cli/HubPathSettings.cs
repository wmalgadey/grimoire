using System.ComponentModel;
using Grimoire.Hub.Runtime.Paths;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Shared base settings declaring every ADR-009 path switch (research.md D4), inherited
/// by every command's own settings class so all commands accept the same server-location
/// switches the web host does (018-hub-cli-commands: commands now resolve the data
/// directory in-process).
///
/// This is SYNTAX-ONLY on Spectre's side: these properties exist so Spectre's parser
/// accepts the switches and its generated help lists them per-command, but they are
/// never read directly by command code. The values that actually govern path resolution
/// still flow through the pre-existing configuration composition
/// (<c>builder.Configuration.AddCommandLine(args, PathConfigurationSwitchMappingsFactory())</c>
/// in <see cref="HubHostComposition"/>, mirroring <c>Program.cs</c>'s web-host path),
/// preserving ADR-009's CLI &gt; env &gt; appsettings &gt; defaults precedence chain. Binding
/// this class's properties instead would create a second, competing source of truth for
/// path values — deliberately avoided.
///
/// One property per <see cref="PathSwitchCatalog.All"/> entry; a parity test
/// (<c>HubHelpUsageTests</c>, T013) asserts the 1:1 mapping so the two can never
/// independently drift. The <see cref="DescriptionAttribute"/> text below mirrors
/// <see cref="PathSwitchCatalog.All"/>'s descriptions (018-hub-cli-commands T042) so
/// `&lt;command&gt; --help` shows the same wording as the root help's "Server options:"
/// section (contracts/cli-commands.md help contract).
/// </summary>
public class HubPathSettings : CommandSettings
{
    [CommandOption("--base-dir <PATH>")]
    [Description("Base directory all other relative Grimoire paths resolve against.")]
    public string? BaseDir { get; set; }

    [CommandOption("--data-dir <PATH>")]
    [Description("Directory holding runtime data (state DB, secrets, agent instructions).")]
    public string? DataDir { get; set; }

    [CommandOption("--content-root <PATH>")]
    [Description("Root of the wiki content tree (pages, index, log).")]
    public string? ContentRoot { get; set; }

    [CommandOption("--raw-dir <PATH>")]
    [Description("Directory for raw/original source artifacts captured on ingest.")]
    public string? RawDir { get; set; }

    [CommandOption("--state-db <PATH>")]
    [Description("Path to the SQLite operational-state database file.")]
    public string? StateDb { get; set; }

    [CommandOption("--secrets-file <PATH>")]
    [Description("Path to the local secrets/.env file (e.g. provider API keys).")]
    public string? SecretsFile { get; set; }

    [CommandOption("--instructions-dir <PATH>")]
    [Description("Directory containing the Ingest agent's instruction files.")]
    public string? InstructionsDir { get; set; }

    [CommandOption("--agent-worker <PATH>")]
    [Description("Path to the Ingest agent worker executable/DLL.")]
    public string? AgentWorker { get; set; }

    [CommandOption("--query-instructions-dir <PATH>")]
    [Description("Directory containing the Query agent's instruction files.")]
    public string? QueryInstructionsDir { get; set; }

    [CommandOption("--conversations-dir <PATH>")]
    [Description("Directory where Query conversation records are stored.")]
    public string? ConversationsDir { get; set; }

    [CommandOption("--query-agent-worker <PATH>")]
    [Description("Path to the Query agent worker executable/DLL.")]
    public string? QueryAgentWorker { get; set; }

    [CommandOption("--write-locks-dir <PATH>")]
    [Description("Directory used for cross-process write-coordination locks.")]
    public string? WriteLocksDir { get; set; }

    [CommandOption("--findings-dir <PATH>")]
    [Description("Directory where Lint findings reports are stored.")]
    public string? FindingsDir { get; set; }

    [CommandOption("--lint-instructions-dir <PATH>")]
    [Description("Directory containing the Lint agent's instruction files.")]
    public string? LintInstructionsDir { get; set; }

    [CommandOption("--lint-agent-worker <PATH>")]
    [Description("Path to the Lint agent worker executable/DLL.")]
    public string? LintAgentWorker { get; set; }

    [CommandOption("--remediation-tasks-dir <PATH>")]
    [Description("Directory where remediation task records are stored.")]
    public string? RemediationTasksDir { get; set; }
}
