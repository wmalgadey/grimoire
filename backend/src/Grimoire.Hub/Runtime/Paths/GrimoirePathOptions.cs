namespace Grimoire.Hub.Runtime.Paths;

/// <summary>
/// Raw configuration input for runtime path composition (ADR-022), bound from the
/// <c>Grimoire:Paths</c> configuration section. Every value is a string that is either
/// absolute (used verbatim) or relative (resolved by <see cref="GrimoirePathResolver"/>
/// against the field's documented anchor — never here).
///
/// <see cref="DataDir"/>, <see cref="WikiDir"/> and <see cref="AgentDir"/> are the three
/// roots exposed on the command line (<see cref="PathSwitchCatalog"/>); every other field
/// is a configuration-file-only sub-path (FR-015, rule R1) with no switch. There is no
/// code-level default for any of them: <c>appsettings.json</c> is the sole source of
/// defaults (FR-005), enforced by rule R2 — see data-model.md §1.
/// </summary>
public sealed class GrimoirePathOptions
{
    public const string SectionName = "Grimoire:Paths";

    /// <summary>
    /// Root for all harness runtime state (raw intake, state DB, write-locks) and, by
    /// default, the agent directory. Anchor: process working directory. Required — no
    /// code default.
    /// </summary>
    public string? DataDir { get; set; }

    /// <summary>
    /// Root for all agent-produced results (wiki pages, index.md, log.md, tasks,
    /// conversations, findings, remediation tasks). Anchor: process working directory,
    /// independent of <see cref="DataDir"/> (a cwd-anchored sibling, not nested beneath
    /// it — spec US3 AS2). Required — no code default.
    /// </summary>
    public string? WikiDir { get; set; }

    /// <summary>
    /// Directory holding the complete agent runtime — worker binaries, dependency
    /// assemblies and instruction files — for every agent type, in per-agent-type
    /// subfolders. Anchor: the resolved <see cref="DataDir"/>. Produced and refreshed by
    /// the agent build (backend/Directory.Build.targets), never written by the hub.
    /// Required — no code default.
    /// </summary>
    public string? AgentDir { get; set; }

    /// <summary>Raw intake storage. Anchor: <see cref="DataDir"/>.</summary>
    public string? RawDir { get; set; }

    /// <summary>SQLite operational state (ADR-003). Anchor: <see cref="DataDir"/>.</summary>
    public string? StateDb { get; set; }

    /// <summary>
    /// Cross-process write-coordination lock directory (ADR-015) — one empty lock file
    /// per contested wiki target, named by SHA-256 of its canonical path. Anchor:
    /// <see cref="DataDir"/>.
    /// </summary>
    public string? WriteLocksDir { get; set; }

    /// <summary>Task artifact storage — agent output. Anchor: <see cref="WikiDir"/>.</summary>
    public string? TasksDir { get; set; }

    /// <summary>
    /// Conversation Record storage (ADR-014) — agent output, one append-only file per
    /// conversation. Anchor: <see cref="WikiDir"/>.
    /// </summary>
    public string? ConversationsDir { get; set; }

    /// <summary>
    /// Findings Report storage (ADR-003) — agent output, one file per Lint Run. Anchor:
    /// <see cref="WikiDir"/>.
    /// </summary>
    public string? FindingsDir { get; set; }

    /// <summary>
    /// Remediation Task Record storage (ADR-018/ADR-014) — agent output, one append-only
    /// file per remediation action task. Anchor: <see cref="WikiDir"/>.
    /// </summary>
    public string? RemediationTasksDir { get; set; }

    /// <summary>
    /// ADR-004 secrets/.env file. Anchor: process working directory (the project root) —
    /// deliberately NOT anchored at any of the three roots, so relocating runtime data,
    /// the agent directory, or the wiki never separates an operator from their
    /// credentials (FR-019).
    /// </summary>
    public string? SecretsFile { get; set; }

    /// <summary>
    /// 018-hub-cli-commands (ADR-020): the exclusive cross-process lock file
    /// <c>Grimoire.Hub.LintDispatch.LintRunCoordinator.TriggerAsync</c> holds for a Lint
    /// Run's full duration, on both the HTTP and CLI entry paths. Not independently
    /// configurable (no <see cref="GrimoirePathOptions"/> field, no switch) — always
    /// <c>lint.pid</c> under the data directory, mirroring how <c>index.md</c>/
    /// <c>log.md</c> are fixed filenames under an already-configurable directory rather
    /// than their own switch.
    /// </summary>
    public const string DefaultLintPidFileName = "lint.pid";

    // Fixed, non-configurable worker filenames (ADR-022 FR-008): the agent-type subfolder
    // name and the worker filename are part of the agent build contract, not a value an
    // operator ever sets. Not a `const`: NetArchTest's HaveDependencyOn scan treats string
    // *field constants* as candidate dependency evidence, and this filename's
    // "Grimoire.IngestAgent" prefix would otherwise false-positive
    // HubAgentDispatchBoundaryRuleTests (ADR-002) even though it is a child-process
    // executable name, not an assembly reference.
    public static readonly string DefaultAgentWorkerFileName = "Grimoire.IngestAgent" + ".dll";

    // Same rationale as DefaultAgentWorkerFileName above, for Grimoire.QueryAgent.
    public static readonly string DefaultQueryAgentWorkerFileName = "Grimoire.QueryAgent" + ".dll";

    // Same rationale as DefaultAgentWorkerFileName above, for Grimoire.LintAgent.
    public static readonly string DefaultLintAgentWorkerFileName = "Grimoire.LintAgent" + ".dll";
}
