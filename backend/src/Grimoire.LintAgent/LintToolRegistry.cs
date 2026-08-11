using Grimoire.AgentRuntime.Guardrails;

namespace Grimoire.LintAgent;

/// <summary>
/// The Lint agent's tool registry: <c>list_files</c>, <c>read_file</c>, and
/// <c>write_file</c> — all three, unlike Query's pre-ADR-015 read-only shape. Lint's
/// write capability is scoped entirely by policy (<c>data/agents/lint/policy.json</c>'s
/// single <c>frontmatter-only</c> rule on <c>.</c>, ADR-016) and by the shared
/// cross-process coordination guard inside
/// <see cref="Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor"/> — never by this
/// registry omitting the tool.
/// </summary>
public static class LintToolRegistry
{
    public static readonly ToolRegistry Default = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.ReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
    ]);
}
