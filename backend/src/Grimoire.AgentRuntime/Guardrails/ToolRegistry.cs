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
    /// Unchanged, deliberately, even now that <c>GuardedToolExecutor</c>'s dispatch
    /// implements ranged reads (T048/T049). <c>LintToolRegistry.Default</c> declares this
    /// exact constant for <c>read_file</c> today, so widening its <c>InputSchemaJson</c>
    /// here would immediately offer <c>offset</c>/<c>limit</c>/<c>frontmatter_only</c> to
    /// the live Lint agent — a capability its <c>system-prompt.md</c> says nothing about
    /// yet (that instruction-file update is T065, deferred to Phase N/layer 08 alongside
    /// this feature's other agent-judgment work). Verified this is <em>not</em> an
    /// eval-replay-fingerprint concern the way <c>search_files</c>/<c>batch</c>/
    /// <c>delete_file</c>'s deferrals are: <c>ReplayModelClient</c> matches a recording by
    /// <c>tool.Name</c> alone (<c>tools.Select(t => t.Name)</c>), never by schema content,
    /// so changing this schema would not itself break any recorded-replay eval. The reason
    /// to wait is simpler and still real: don't hand the live model a parameter its
    /// instructions don't yet explain how to use.
    /// <see cref="RangedReadFileDefinition"/> is the schema variant that advertises the new
    /// parameters; only test registries and, once the recapture layer flips it for Lint
    /// (alongside T065), <c>LintToolRegistry.Default</c> reference it. The dispatch logic
    /// in <c>GuardedToolExecutor</c> does not care which definition advertised the call —
    /// it parses whatever JSON properties are present — so nothing here gates correctness,
    /// only what the provider's strict tool-use validation (#127) lets through in
    /// production.
    /// </summary>
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
          "required": ["path"],
          "additionalProperties": false
        }
        """);

    /// <summary>
    /// ADR-030 R3 (026-guarded-tool-surface): the ranged-read-capable <c>read_file</c>
    /// schema, offering the optional <c>offset</c>/<c>limit</c>/<c>frontmatter_only</c>
    /// parameters T048/T049 implement in <c>GuardedToolExecutor</c>. Not yet referenced by
    /// any production agent registry — see <see cref="ReadFileDefinition"/>'s doc comment
    /// for why the switch waits for the eval-recapture layer.
    /// </summary>
    public static readonly ToolDefinition RangedReadFileDefinition = new(
        Name: ReadFile,
        Description: "Read a file inside the allowed read scope, in full or as a bounded " +
            "slice: a 1-based line range (like 'sed -n' / 'head'), or just its frontmatter.",
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
              "description": "1-based first line to return. Omit for a whole-file read."
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
            "one turn, at most 20. Each is evaluated and recorded individually; a batch " +
            "containing a write, a delete, a nested batch, or more than 20 calls runs nothing.",
        // `input` is the union of the three batchable tools' own parameters rather than a
        // free-form object, for the same reason `maxItems` is absent below: the Anthropic
        // tool-use API refuses a bare `{"type": "object"}` ("for 'object' type,
        // 'additionalProperties' must be explicitly set to false"), and `additionalProperties:
        // false` is only meaningful alongside the properties it is closing over. The executor
        // still dispatches on the named tool and reads only the properties that tool takes, so
        // this schema constrains what the provider will send without widening what is accepted.
        //
        // The 20-call cap is stated in the description and enforced by GuardedToolExecutor
        // (`BatchMaxCalls`, denial reason `too_many_calls`) — deliberately NOT expressed as
        // the `maxItems` keyword it naturally maps to, because the Anthropic tool-use API
        // rejects the whole request when a custom tool's schema carries it: "For 'array'
        // type, property 'maxItems' is not supported" (a 400 that fails the run before the
        // first turn, not a per-call error). This surfaced the moment LintToolRegistry began
        // declaring `batch` to a live provider (T012); the executor was already the
        // authority on the limit, so nothing about enforcement changed with its removal.
        InputSchemaJson: """
        {
          "type": "object",
          "properties": {
            "calls": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "tool": {
                    "type": "string",
                    "enum": ["list_files", "read_file", "search_files"]
                  },
                  "input": {
                    "type": "object",
                    "description": "Arguments for the chosen tool, exactly as that tool takes them.",
                    "properties": {
                      "path": { "type": "string" },
                      "pattern": { "type": "string" },
                      "ignore_case": { "type": "boolean" },
                      "max_results": { "type": "integer" },
                      "offset": { "type": "integer" },
                      "limit": { "type": "integer" },
                      "frontmatter_only": { "type": "boolean" }
                    },
                    "additionalProperties": false
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
              "description": "Full new file content (UTF-8 markdown) when mode is omitted/\"replace\" (default); the new entry only, when mode is \"prepend\"."
            },
            "mode": {
              "type": "string",
              "enum": ["replace", "prepend"],
              "description": "\"replace\" (default): content is the full proposed file. \"prepend\": content is a new entry only — the harness reads the target's current content itself and commits entry + currentContent, so the caller never has to reproduce the whole file to add one entry."
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
