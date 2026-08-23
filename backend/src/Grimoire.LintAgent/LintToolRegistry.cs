using Grimoire.AgentRuntime.Guardrails;

namespace Grimoire.LintAgent;

/// <summary>
/// The Lint agent's tool registry. Since 026-guarded-tool-surface (ADR-030/ADR-031) this is
/// the full guarded surface: <c>list_files</c>, <c>read_file</c> (ranged — see below),
/// <c>search_files</c>, <c>batch</c>, <c>write_file</c> and <c>delete_file</c>.
///
/// Lint's authority is scoped entirely by policy
/// (<c>Grimoire.LintAgent/Instructions/policy.json</c> v2: one <c>read-write</c> rule on
/// <c>.</c> and one <c>delete</c> scope on <c>.</c>, FR-014/FR-015/FR-016/FR-016a) and by
/// the shared cross-process coordination guard inside
/// <see cref="Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor"/> — never by this
/// registry omitting a tool. One registry serves the survey run and both remediation
/// execution paths alike; the harness draws no mode distinction (FR-017).
///
/// <c>read_file</c> is declared with <see cref="ToolRegistry.RangedReadFileDefinition"/>,
/// not <see cref="ToolRegistry.ReadFileDefinition"/> — same tool name, wider schema: it
/// advertises the optional <c>offset</c>/<c>limit</c>/<c>frontmatter_only</c> parameters
/// (ADR-030 R3). <c>GuardedToolExecutor</c> parses whatever properties are present
/// regardless of which definition advertised the call, so this choice governs only what the
/// provider's strict tool-use validation lets through — and it is made here, together with
/// the <c>system-prompt.md</c> guidance (T065) that explains when to use a slice, so the
/// model is never offered a parameter its instructions do not cover.
///
/// Declaring these tools changes the exact tool-name set offered to the model on every
/// turn, which every recorded-replay Lint/Remediation eval scenario fingerprints. This
/// registry, <c>policy.json</c> v2 and <c>system-prompt.md</c> therefore land together with
/// the one-time eval recapture (ADR-012) — see 026-guarded-tool-surface tasks.md, Phase N.
/// </summary>
public static class LintToolRegistry
{
    public static readonly ToolRegistry Default = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.RangedReadFileDefinition,
        ToolRegistry.SearchFilesDefinition,
        ToolRegistry.BatchDefinition,
        ToolRegistry.WriteFileDefinition,
        ToolRegistry.DeleteFileDefinition,
    ]);
}
