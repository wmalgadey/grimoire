namespace Grimoire.Hub.Runtime.Paths;

/// <summary>
/// Raw configuration input for runtime path composition (ADR-009), bound from the
/// <c>Grimoire:Paths</c> configuration section. Every value is optional; an empty or
/// absent value means "use the documented code default" (data-model.md). Relative
/// values are resolved by <see cref="GrimoirePathResolver"/> — never here.
/// </summary>
public sealed class GrimoirePathOptions
{
    public const string SectionName = "Grimoire:Paths";

    /// <summary>Root for every other relative default. Default: process working directory.</summary>
    public string? BaseDir { get; set; }

    /// <summary>
    /// Wiki content root — deliberately OUTSIDE the data directory so it can be
    /// committed to its own git repository. Default: <c>wiki</c> under the base.
    /// </summary>
    public string? ContentRoot { get; set; }

    /// <summary>The consolidated internal runtime data directory. Default: <c>data</c> under the base.</summary>
    public string? DataDir { get; set; }

    /// <summary>Raw intake storage. Default: <c>raw</c> under the data directory.</summary>
    public string? RawDir { get; set; }

    /// <summary>SQLite operational state (ADR-003). Default: <c>state/operational-state.db</c> under the data directory.</summary>
    public string? StateDb { get; set; }

    /// <summary>ADR-004 secrets file. Default: <c>.env</c> under the data directory.</summary>
    public string? SecretsFile { get; set; }

    /// <summary>ADR-007 instruction surface directory. Default: <c>agents/ingest</c> under the data directory.</summary>
    public string? InstructionsDir { get; set; }

    /// <summary>
    /// Query agent instruction surface directory (008-query-agent, ADR-009/ADR-011).
    /// Default: <c>agents/query</c> under the data directory.
    /// </summary>
    public string? QueryInstructionsDir { get; set; }

    /// <summary>
    /// Lint agent instruction surface directory (013-lint-agent, ADR-007/ADR-009).
    /// Default: <c>agents/lint</c> under the data directory.
    /// </summary>
    public string? LintInstructionsDir { get; set; }

    /// <summary>
    /// Conversation Record storage (011-query-conversations, ADR-014) — Hub-written
    /// only, git-ignored, one append-only file per conversation. Default:
    /// <c>conversations</c> under the data directory.
    /// </summary>
    public string? ConversationsDir { get; set; }

    /// <summary>
    /// Cross-process write-coordination lock directory (012-query-synthesis-writes,
    /// ADR-015) — one empty lock file per contested wiki target, named by SHA-256 of its
    /// canonical path. Outside <c>wiki/</c> and git, git-ignored. Default:
    /// <c>write-locks</c> under the data directory.
    /// </summary>
    public string? WriteLocksDir { get; set; }

    /// <summary>
    /// Findings Report storage (013-lint-agent, ADR-009/ADR-003) — Hub-written only,
    /// git-ignored, one file per Lint Run. Outside <c>wiki/</c> and git. Default:
    /// <c>findings</c> under the data directory.
    /// </summary>
    public string? FindingsDir { get; set; }

    /// <summary>Ingest agent worker (.csproj/.dll/executable). Default: beside the Hub binaries.</summary>
    public string? AgentWorker { get; set; }

    /// <summary>Query agent worker (.csproj/.dll/executable). Default: beside the Hub binaries.</summary>
    public string? QueryAgentWorker { get; set; }

    /// <summary>Lint agent worker (.csproj/.dll/executable). Default: beside the Hub binaries.</summary>
    public string? LintAgentWorker { get; set; }

    public const string DefaultContentRootDirName = "wiki";
    public const string DefaultDataDirName = "data";
    public const string DefaultRawDirName = "raw";
    public const string DefaultSecretsFileName = ".env";
    public const string DefaultConversationsDirName = "conversations";
    public const string DefaultWriteLocksDirName = "write-locks";
    public const string DefaultFindingsDirName = "findings";

    // Not a `const`: NetArchTest's HaveDependencyOn scan treats string *field constants*
    // as candidate dependency evidence, and this filename's "Grimoire.IngestAgent" prefix
    // would otherwise false-positive HubAgentDispatchBoundaryRuleTests (ADR-002) even
    // though it is a child-process executable name, not an assembly reference.
    public static readonly string DefaultAgentWorkerFileName = "Grimoire.IngestAgent" + ".dll";

    // Same rationale as DefaultAgentWorkerFileName above, for Grimoire.QueryAgent.
    public static readonly string DefaultQueryAgentWorkerFileName = "Grimoire.QueryAgent" + ".dll";

    // Same rationale as DefaultAgentWorkerFileName above, for Grimoire.LintAgent (013-lint-agent).
    public static readonly string DefaultLintAgentWorkerFileName = "Grimoire.LintAgent" + ".dll";

    public static readonly string DefaultStateDbRelativePath = Path.Combine("state", "operational-state.db");
    public static readonly string DefaultInstructionsDirRelativePath = Path.Combine("agents", "ingest");
    public static readonly string DefaultQueryInstructionsDirRelativePath = Path.Combine("agents", "query");
    public static readonly string DefaultLintInstructionsDirRelativePath = Path.Combine("agents", "lint");
}
