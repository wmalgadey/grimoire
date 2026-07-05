using Grimoire.IngestAgent.AgentCore;

namespace Grimoire.IngestAgent.Guardrails;

/// <summary>
/// The three guarded tool definitions offered to the model on every turn.
/// Schemas are exactly per <c>contracts/guarded-tools.md</c>.
/// </summary>
public static class ToolRegistry
{
    public const string ListFiles = "list_files";
    public const string ReadFile = "read_file";
    public const string WriteFile = "write_file";

    public static readonly IReadOnlyList<ToolDefinition> All =
    [
        new ToolDefinition(
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
            """),

        new ToolDefinition(
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
            """),

        new ToolDefinition(
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
            """),
    ];
}
