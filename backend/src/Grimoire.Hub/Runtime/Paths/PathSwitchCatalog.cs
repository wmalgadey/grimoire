namespace Grimoire.Hub.Runtime.Paths;

/// <summary>
/// A single ADR-009 command-line switch — pairs the CLI switch name with its
/// configuration key (for AddCommandLine) and its human-readable description (for
/// --help output), so all three can never independently drift.
/// </summary>
internal sealed record PathSwitch(string Name, string ConfigKey, string Description);

/// <summary>
/// Single source of truth for the ADR-009 switch vocabulary (017-hub-help-usage,
/// T009). Both Program.cs's AddCommandLine wiring and its --help usage text, as well
/// as HubHelpUsageTests' parity assertions, derive from this one list — internal
/// visibility plus [assembly: InternalsVisibleTo("Grimoire.IntegrationTests")]
/// (AssemblyInfo.cs) lets the test reference it directly instead of maintaining a
/// second hardcoded copy.
/// </summary>
internal static class PathSwitchCatalog
{
    public static readonly IReadOnlyList<PathSwitch> All =
    [
        new("--base-dir", "Grimoire:Paths:BaseDir", "Base directory all other relative Grimoire paths resolve against."),
        new("--data-dir", "Grimoire:Paths:DataDir", "Directory holding runtime data (state DB, secrets, agent instructions)."),
        new("--content-root", "Grimoire:Paths:ContentRoot", "Root of the wiki content tree (pages, index, log)."),
        new("--raw-dir", "Grimoire:Paths:RawDir", "Directory for raw/original source artifacts captured on ingest."),
        new("--state-db", "Grimoire:Paths:StateDb", "Path to the SQLite operational-state database file."),
        new("--secrets-file", "Grimoire:Paths:SecretsFile", "Path to the local secrets/.env file (e.g. provider API keys)."),
        new("--instructions-dir", "Grimoire:Paths:InstructionsDir", "Directory containing the Ingest agent's instruction files."),
        new("--agent-worker", "Grimoire:Paths:AgentWorker", "Path to the Ingest agent worker executable/DLL."),
        new("--query-instructions-dir", "Grimoire:Paths:QueryInstructionsDir", "Directory containing the Query agent's instruction files."),
        new("--conversations-dir", "Grimoire:Paths:ConversationsDir", "Directory where Query conversation records are stored."),
        new("--query-agent-worker", "Grimoire:Paths:QueryAgentWorker", "Path to the Query agent worker executable/DLL."),
        new("--write-locks-dir", "Grimoire:Paths:WriteLocksDir", "Directory used for cross-process write-coordination locks."),
        new("--findings-dir", "Grimoire:Paths:FindingsDir", "Directory where Lint findings reports are stored."),
        new("--lint-instructions-dir", "Grimoire:Paths:LintInstructionsDir", "Directory containing the Lint agent's instruction files."),
        new("--lint-agent-worker", "Grimoire:Paths:LintAgentWorker", "Path to the Lint agent worker executable/DLL."),
        new("--remediation-tasks-dir", "Grimoire:Paths:RemediationTasksDir", "Directory where remediation task records are stored."),
    ];
}
