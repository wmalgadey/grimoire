using Grimoire.AgentRuntime.Core;

namespace Grimoire.AgentRuntime.Guardrails;

/// <summary>
/// The set of tool definitions offered to the model on a run, and the tool-name lookup
/// used by <see cref="GuardedToolExecutor"/> to decide whether a requested tool name is
/// one this run actually supports (ADR-011 R3/R11): a tool name the registry does not
/// support is rejected as unknown even if a hardcoded dispatch case for it exists, so an
/// agent process configured with a read-only registry (e.g. Grimoire.QueryAgent) can
/// never reach a write branch regardless of what the model requests.
/// <para>
/// #127: every schema declares <c>additionalProperties: false</c> alongside its
/// <c>required</c> list, which is what strict tool use needs in order for the provider to
/// guarantee that <c>tool_use.input</c> validates before it reaches us. That is a statement
/// about the <em>shape</em> of a tool call and nothing more. Authorization stays entirely
/// with <see cref="GuardedToolExecutor"/> and the policy (Principle V, deny-by-default at
/// the tool boundary): a schema-valid <c>write_file</c> aimed at a forbidden path is still
/// denied here, and the provider has no say in it.
/// </para>
/// </summary>
public sealed class ToolRegistry
{
    public const string ListFiles = "list_files";
    public const string ReadFile = "read_file";
    public const string WriteFile = "write_file";

    /// <summary>ADR-030 R1 (026-guarded-tool-surface): mimics <c>grep -rn</c>.</summary>
    public const string SearchFiles = "search_files";

    /// <summary>ADR-030 R4 (026-guarded-tool-surface): read-only calls evaluated and
    /// recorded individually; a write, a delete, or a nested batch rejects the whole
    /// call before any member executes.</summary>
    public const string Batch = "batch";

    /// <summary>ADR-031 R3 (026-guarded-tool-surface): mimics <c>rm</c>. Evaluated against
    /// the <c>delete</c> scope, never the write scope.</summary>
    public const string DeleteFile = "delete_file";

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
          "required": ["path"],
          "additionalProperties": false
        }
        """);

    /// <summary>
    /// ADR-030 R3 (026-guarded-tool-surface): unchanged default (whole-file read, byte for
    /// byte) with <c>offset</c>/<c>limit</c>/<c>frontmatter_only</c> now optional — omitting
    /// all three is exactly today's behavior, including setting the write-coordination
    /// baseline. Supplying any of them makes the read partial, which must never set that
    /// baseline (FR-010).
    /// </summary>
    public static readonly ToolDefinition ReadFileDefinition = new(
        Name: ReadFile,
        Description: "Read the content of a file inside the allowed read scope. With no other " +
            "parameters, returns the whole file. 'offset'/'limit' return a line range " +
            "(sed -n 'X,Yp'); 'frontmatter_only' returns just the frontmatter block.",
        InputSchemaJson: """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "File path relative to the repository root."
            },
            "offset": {
              "type": "integer",
              "description": "1-based first line to return."
            },
            "limit": {
              "type": "integer",
              "description": "Maximum number of lines to return."
            },
            "frontmatter_only": {
              "type": "boolean",
              "description": "Return only the frontmatter block. Default false."
            }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """);

    /// <summary>ADR-030 R1 (026-guarded-tool-surface): mimics <c>grep -rn</c>. Every
    /// candidate path is evaluated against the read policy before the file is opened; a
    /// match in an out-of-scope file is omitted silently, never reported.</summary>
    public static readonly ToolDefinition SearchFilesDefinition = new(
        Name: SearchFiles,
        Description: "Search file contents inside the allowed read scope for a regular " +
            "expression, like 'grep -rn'. Non-backtracking syntax: no lookaround or " +
            "backreferences.",
        InputSchemaJson: """
        {
          "type": "object",
          "properties": {
            "pattern": {
              "type": "string",
              "description": "Regular expression. Non-backtracking syntax: no lookaround or backreferences."
            },
            "path": {
              "type": "string",
              "description": "Optional directory or file prefix to narrow the search, relative to the content root."
            },
            "ignore_case": {
              "type": "boolean",
              "description": "Case-insensitive matching. Default false."
            },
            "max_results": {
              "type": "integer",
              "description": "Cap on returned matches. Default 200, maximum 1000."
            }
          },
          "required": ["pattern"],
          "additionalProperties": false
        }
        """);

    /// <summary>ADR-030 R4 (026-guarded-tool-surface): read-only calls only — the enum
    /// makes a write unrepresentable at the schema level; the executor rejects one again at
    /// runtime, because the schema is the provider's guarantee and the executor is
    /// ours.</summary>
    public static readonly ToolDefinition BatchDefinition = new(
        Name: Batch,
        Description: "Run several read-only calls (list_files, read_file, search_files) in " +
            "one turn. Each is evaluated and recorded individually; a batch containing a " +
            "write, a delete, or a nested batch runs nothing.",
        InputSchemaJson: """
        {
          "type": "object",
          "properties": {
            "calls": {
              "type": "array",
              "maxItems": 20,
              "items": {
                "type": "object",
                "properties": {
                  "tool": {
                    "type": "string",
                    "enum": ["list_files", "read_file", "search_files"]
                  },
                  "input": {
                    "type": "object"
                  }
                },
                "required": ["tool", "input"],
                "additionalProperties": false
              }
            }
          },
          "required": ["calls"],
          "additionalProperties": false
        }
        """);

    /// <summary>ADR-031 R3 (026-guarded-tool-surface): mimics <c>rm</c>. Evaluated against
    /// the delete scope, never the write scope; journaled before removal so a later failure
    /// in the same run restores it.</summary>
    public static readonly ToolDefinition DeleteFileDefinition = new(
        Name: DeleteFile,
        Description: "Delete a file inside the allowed delete scope, like 'rm'.",
        InputSchemaJson: """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "File to delete, relative to the content root."
            }
          },
          "required": ["path"],
          "additionalProperties": false
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
          "required": ["path", "content"],
          "additionalProperties": false
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
