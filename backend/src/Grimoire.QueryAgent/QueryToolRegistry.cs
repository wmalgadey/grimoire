using Grimoire.AgentRuntime.Guardrails;

namespace Grimoire.QueryAgent;

/// <summary>
/// The Query agent's tool registry: exactly <c>list_files</c> and <c>read_file</c>
/// (contracts/guarded-read-only-tools.md). Deliberately does not reference or import
/// any write-tool type or constant at all (FR-011, ADR-011 R3) — the structural half of
/// "no write capability" is that this file has nothing to reference, not merely that it
/// chooses not to register one.
/// </summary>
public static class QueryToolRegistry
{
    public static readonly ToolRegistry Default = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.ReadFileDefinition,
    ]);
}
