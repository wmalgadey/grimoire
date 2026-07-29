using Grimoire.AgentRuntime.Guardrails;

namespace Grimoire.IngestAgent;

/// <summary>
/// The Ingest agent's tool registry — the explicit profile declaration of exactly
/// today's tool set (FR-004: effective capabilities == this declaration): the
/// historical three tools <c>list_files</c>, <c>read_file</c>, <c>write_file</c> with
/// unchanged schemas. Replaces the former implicit reliance on
/// <see cref="ToolRegistry.Default"/> in the composition root; the definitions (and
/// their order, hence the tool list offered to the model) are identical (FR-008).
/// </summary>
public static class IngestToolRegistry
{
    public static readonly ToolRegistry Default = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.ReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
    ]);
}
