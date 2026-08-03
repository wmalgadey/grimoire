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
/// independently drift.
/// </summary>
public class HubPathSettings : CommandSettings
{
    [CommandOption("--base-dir <PATH>")]
    public string? BaseDir { get; set; }

    [CommandOption("--data-dir <PATH>")]
    public string? DataDir { get; set; }

    [CommandOption("--content-root <PATH>")]
    public string? ContentRoot { get; set; }

    [CommandOption("--raw-dir <PATH>")]
    public string? RawDir { get; set; }

    [CommandOption("--state-db <PATH>")]
    public string? StateDb { get; set; }

    [CommandOption("--secrets-file <PATH>")]
    public string? SecretsFile { get; set; }

    [CommandOption("--instructions-dir <PATH>")]
    public string? InstructionsDir { get; set; }

    [CommandOption("--agent-worker <PATH>")]
    public string? AgentWorker { get; set; }

    [CommandOption("--query-instructions-dir <PATH>")]
    public string? QueryInstructionsDir { get; set; }

    [CommandOption("--conversations-dir <PATH>")]
    public string? ConversationsDir { get; set; }

    [CommandOption("--query-agent-worker <PATH>")]
    public string? QueryAgentWorker { get; set; }

    [CommandOption("--write-locks-dir <PATH>")]
    public string? WriteLocksDir { get; set; }

    [CommandOption("--findings-dir <PATH>")]
    public string? FindingsDir { get; set; }

    [CommandOption("--lint-instructions-dir <PATH>")]
    public string? LintInstructionsDir { get; set; }

    [CommandOption("--lint-agent-worker <PATH>")]
    public string? LintAgentWorker { get; set; }

    [CommandOption("--remediation-tasks-dir <PATH>")]
    public string? RemediationTasksDir { get; set; }
}
