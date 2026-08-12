namespace Grimoire.Hub.Runtime.Paths;

/// <summary>
/// Root for all harness runtime state (raw intake, state DB, write-locks). Anchor:
/// process working directory, independent of the other three roots (PR #55 reviewer
/// confirmation). <see cref="Dir"/> is required — no code default (ADR-022 R2).
/// </summary>
public sealed class DataPathOptions
{
    public string? Dir { get; set; }

    /// <summary>Raw intake storage. Anchor: <see cref="Dir"/>.</summary>
    public string? RawDir { get; set; }

    /// <summary>SQLite operational state (ADR-003). Anchor: <see cref="Dir"/>.</summary>
    public string? StateDb { get; set; }

    /// <summary>
    /// Cross-process write-coordination lock directory (ADR-015) — one empty lock file
    /// per contested wiki target, named by SHA-256 of its canonical path. Anchor:
    /// <see cref="Dir"/>.
    /// </summary>
    public string? WriteLocksDir { get; set; }
}

/// <summary>
/// Root for the wiki content itself — index.md, log.md, and topical article folders.
/// Anchor: process working directory, independent of the other three roots (a
/// cwd-anchored sibling, not nested beneath any of them — spec US3 AS2). No longer holds
/// agent process bookkeeping as of 022-memory-directory-root — that moved to
/// <see cref="MemoryPathOptions"/>. <see cref="Dir"/> is required — no code default.
/// </summary>
public sealed class WikiPathOptions
{
    public string? Dir { get; set; }
}

/// <summary>
/// Directory holding the complete agent runtime — worker binaries, dependency assemblies
/// and instruction files — for every agent type, in per-agent-type subfolders. Anchor:
/// process working directory, independent of the other three roots (PR #55 reviewer
/// confirmation). Produced and refreshed by the agent build
/// (backend/Directory.Build.targets), never written by the hub. <see cref="Dir"/> is
/// required — no code default.
/// </summary>
public sealed class AgentPathOptions
{
    public string? Dir { get; set; }
}

/// <summary>
/// Root for agent process bookkeeping — task artifacts, conversation records, lint
/// findings reports, remediation task records (022-memory-directory-root, ADR-024).
/// Anchor: process working directory, independent of the other three roots. Shares no
/// parent with, and is never nested inside, <see cref="DataPathOptions"/>,
/// <see cref="WikiPathOptions"/> or <see cref="AgentPathOptions"/> unless an operator
/// explicitly configures it that way. <see cref="Dir"/> is required — no code default
/// (ADR-024 M2).
/// </summary>
public sealed class MemoryPathOptions
{
    public string? Dir { get; set; }

    /// <summary>Task artifact storage — agent output. Anchor: <see cref="Dir"/>.</summary>
    public string? TasksDir { get; set; }

    /// <summary>
    /// Conversation Record storage (ADR-014) — agent output, one append-only file per
    /// conversation. Anchor: <see cref="Dir"/>.
    /// </summary>
    public string? ConversationsDir { get; set; }

    /// <summary>
    /// Findings Report storage (ADR-003) — agent output, one file per Lint Run. Anchor:
    /// <see cref="Dir"/>.
    /// </summary>
    public string? FindingsDir { get; set; }

    /// <summary>
    /// Remediation Task Record storage (ADR-018/ADR-014) — agent output, one append-only
    /// file per remediation action task. Anchor: <see cref="Dir"/>.
    /// </summary>
    public string? RemediationTasksDir { get; set; }
}

/// <summary>
/// Raw configuration input for runtime path composition (ADR-022, amended by ADR-024),
/// bound from the <c>Grimoire:Paths</c> configuration section. Every leaf value is a
/// string that is either absolute (used verbatim) or relative (resolved by
/// <see cref="GrimoirePathResolver"/> against the field's documented anchor — never
/// here).
///
/// The type is a **graph of four anchor groups plus one ungrouped property**
/// (<see cref="Data"/>, <see cref="Wiki"/>, <see cref="Agent"/>, <see cref="Memory"/>,
/// <see cref="SecretsFile"/>), mirroring the grouped <c>appsettings.json</c> shape
/// (ADR-024 rule M4, research R8) — not a flat bag. <see cref="Data"/>.<c>Dir</c>,
/// <see cref="Wiki"/>.<c>Dir</c>, <see cref="Agent"/>.<c>Dir</c> and
/// <see cref="Memory"/>.<c>Dir</c> are the four roots exposed on the command line
/// (<see cref="PathSwitchCatalog"/>); every other leaf is a configuration-file-only
/// sub-path with no switch. There is no code-level default for any of them:
/// <c>appsettings.json</c> is the sole source of defaults (FR-006), enforced by rule R2 /
/// ADR-024 rule M2 — see data-model.md §2.
///
/// Each group property is initialized (<c>= new()</c>) so an entirely absent JSON group
/// binds to an empty group rather than null — a null-guard, not a path default. Every
/// leaf path property stays <c>string?</c> with no initializer.
/// </summary>
public sealed class GrimoirePathOptions
{
    public const string SectionName = "Grimoire:Paths";

    public DataPathOptions Data { get; set; } = new();

    public WikiPathOptions Wiki { get; set; } = new();

    public AgentPathOptions Agent { get; set; } = new();

    public MemoryPathOptions Memory { get; set; } = new();

    /// <summary>
    /// ADR-004 secrets/.env file. Anchor: process working directory (the project root) —
    /// deliberately NOT anchored at, or grouped with, any of the four roots, so
    /// relocating runtime data, the agent directory, the wiki, or the memory folder never
    /// separates an operator from their credentials.
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
