using Grimoire.AgentRuntime.Guardrails;

namespace Grimoire.LintAgent;

/// <summary>
/// The Lint agent's tool registry: <c>list_files</c>, <c>read_file</c>, <c>write_file</c>,
/// and — ADR-030/ADR-031 (026-guarded-tool-surface) — <c>search_files</c>, <c>batch</c>, and
/// <c>delete_file</c>. Lint's write and delete capabilities are scoped entirely by policy
/// (<c>Grimoire.LintAgent/Instructions/policy.json</c>'s <c>read-write</c> and <c>delete</c>
/// rules on <c>.</c>, ADR-031) and by the shared cross-process coordination guard inside
/// <see cref="Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor"/> — never by this
/// registry omitting a tool.
///
/// ADR-030 R6: these three new tools are declared here only. Ingest and Query are
/// unchanged and, by ADR-011 R3/R11's unknown-tool rejection, cannot reach them even if the
/// model requests them by name.
/// </summary>
public static class LintToolRegistry
{
    public static readonly ToolRegistry Default = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.ReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
        ToolRegistry.SearchFilesDefinition,
        ToolRegistry.BatchDefinition,
        ToolRegistry.DeleteFileDefinition,
    ]);
}
