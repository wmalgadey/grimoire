using Grimoire.AgentRuntime.Guardrails;

namespace Grimoire.QueryAgent;

/// <summary>
/// The Query agent's tool registry: <c>list_files</c>, <c>read_file</c>, and — since
/// ADR-015 (012-query-synthesis-writes) — <c>write_file</c>. The read-only structural
/// guarantee this file's comment used to describe (no write-tool reference at all) is
/// superseded: Query's write capability is now scoped entirely by policy
/// (<c>data/agents/query/policy.json</c>'s create-only <c>pages/</c> rule plus
/// <c>index.md</c>/<c>log.md</c>) and by the cross-process coordination guard inside
/// <see cref="Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor"/> — never by this
/// registry omitting the tool. <see cref="Grimoire.AgentRuntime.Guardrails.ToolRegistry.Supports"/>
/// still means "this run's registry legitimately includes this tool"; the enforcement
/// question moved from "does Query have write_file at all" to "what may Query's
/// write_file target," per ADR-015's Write Scope.
/// </summary>
public static class QueryToolRegistry
{
    public static readonly ToolRegistry Default = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.ReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
    ]);
}
