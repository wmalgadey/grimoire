using Grimoire.AgentRuntime.Core;

namespace Grimoire.AgentRuntime.Guardrails;

/// <summary>
/// The set of tool definitions offered to the model on a run, and the tool-name lookup
/// used by <see cref="GuardedToolExecutor"/> to decide whether a requested tool name is
/// one this run actually supports (ADR-011 R3/R11): a tool name the registry does not
/// support is rejected as unknown even if a hardcoded dispatch case for it exists, so an
/// agent process configured with a read-only registry (e.g. Grimoire.QueryAgent) can
/// never reach a write branch regardless of what the model requests.
/// </summary>
public sealed class ToolRegistry
{
    public const string ListFiles = "list_files";
    public const string ReadFile = "read_file";
    public const string WriteFile = "write_file";

    public static readonly ToolDefinition ListFilesDefinition = new(
        Name: ListFiles,
        Description: "List files and directories under a path inside the allowed read scope.",
        InputSchemaJson: """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "Directory path relative to the repository root."
            }
          },
          "required": ["path"]
        }
        """);

    public static readonly ToolDefinition ReadFileDefinition = new(
        Name: ReadFile,
        Description: "Read the full content of a file inside the allowed read scope.",
        InputSchemaJson: """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "File path relative to the repository root."
            }
          },
          "required": ["path"]
        }
        """);

    public static readonly ToolDefinition WriteFileDefinition = new(
        Name: WriteFile,
        Description: "Create or overwrite a file inside the allowed write scope with the given content.",
        InputSchemaJson: """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "File path relative to the repository root."
            },
            "content": {
              "type": "string",
              "description": "Full new file content (UTF-8 markdown)."
            }
          },
          "required": ["path", "content"]
        }
        """);

    /// <summary>
    /// The historical three-tool set (list_files, read_file, write_file) — the implicit
    /// default for <see cref="GuardedToolExecutor"/>/<c>AgentLoop</c> when no registry is
    /// explicitly supplied, so Grimoire.IngestAgent's existing call sites are unaffected
    /// by the Query extraction (ADR-011). Grimoire.QueryAgent always supplies its own
    /// explicit two-tool registry with no write tool at all (FR-011).
    /// </summary>
    public static readonly ToolRegistry Default = new([ListFilesDefinition, ReadFileDefinition, WriteFileDefinition]);

    private readonly HashSet<string> _names;

    public ToolRegistry(IReadOnlyList<ToolDefinition> tools)
    {
        Tools = tools;
        _names = new HashSet<string>(tools.Select(t => t.Name), StringComparer.Ordinal);
    }

    public IReadOnlyList<ToolDefinition> Tools { get; }

    /// <summary>Whether this registry offers (and therefore permits dispatching) the named tool.</summary>
    public bool Supports(string toolName) => _names.Contains(toolName);
}
