using Grimoire.AgentRuntime.Guardrails;

namespace Grimoire.LintAgent;

/// <summary>
/// The Lint agent's tool registry: <c>list_files</c>, <c>read_file</c>, and
/// <c>write_file</c> — all three, unlike Query's pre-ADR-015 read-only shape. Lint's
/// write capability is scoped entirely by policy (<c>Grimoire.LintAgent/Instructions/policy.json</c>'s
/// single <c>frontmatter-only</c> rule on <c>.</c>, ADR-016) and by the shared
/// cross-process coordination guard inside
/// <see cref="Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor"/> — never by this
/// registry omitting the tool.
///
/// ADR-030/ADR-031 (026-guarded-tool-surface) add three more tool definitions to
/// <see cref="ToolRegistry"/> (<c>search_files</c>, <c>batch</c>, <c>delete_file</c>), but
/// this registry does not declare them yet: doing so changes the exact tool set offered to
/// the model on every turn, which every existing recorded-replay eval scenario for Lint and
/// Remediation fingerprints — adding them here without a matching recapture makes every one
/// of those recordings replay-mismatch (`tool_names` differs from what was recorded). They
/// are declared together with the recapture, in the layer that also flips
/// <c>policy.json</c> to v2 (see 026-guarded-tool-surface-04-foundations's PR description).
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
