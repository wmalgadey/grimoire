using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-015 (supersedes ADR-011 C7): the Query agent gains a
/// narrow, structurally-guarded write capability (create Synthesis Pages, maintain
/// index/log) instead of no write capability at all. The rule is rewritten from its
/// former "zero reachable writes anywhere" assertion to the allow-listed-namespace shape
/// already used by <see cref="IngestAgentGuardedWriteBoundaryRuleTests"/>: reachable
/// filesystem-write API calls anywhere in Grimoire.QueryAgent (and the shared
/// Grimoire.AgentRuntime library it depends on) are permitted only from types in
/// Grimoire.AgentRuntime.Guardrails — which now also contains the
/// Grimoire.AgentRuntime.Guardrails.Coordination sub-namespace (ADR-015's
/// SharedFileWriteGuard/CrossProcessFileLock), covered by the same prefix match — every
/// other type must still show zero reachable write calls. At Phase 0 (012-query-synthesis-writes
/// T001) this rule passes vacuously: no write tool is wired into QueryToolRegistry yet
/// (that is Phase 3, T022). Proven live by a Red/Green probe (T003): a temporary scratch
/// class calling File.WriteAllText directly (outside the allow-list) is added, confirmed
/// to fail the rule naming the violation, then removed and confirmed green again.
/// </summary>
public class QueryAgentGuardedWriteBoundaryRuleTests
{
    private static readonly System.Reflection.Assembly[] _scannedAssemblies =
    [
        typeof(Grimoire.QueryAgent.QueryToolRegistry).Assembly,
        typeof(Grimoire.AgentRuntime.Guardrails.ToolRegistry).Assembly,
    ];

    // Namespace prefixes permitted to use filesystem-write APIs (ADR-015).
    // Grimoire.AgentRuntime.Guardrails is a prefix match so it also covers the
    // yet-to-exist Coordination sub-namespace (Grimoire.AgentRuntime.Guardrails.Coordination)
    // without a second entry. Adapters.Replay (ADR-012, shared with
    // IngestAgentGuardedWriteBoundaryRuleTests) writes only the captured turn stream to the
    // eval runner's GRIMOIRE_MODEL_CAPTURE_PATH — never wiki content; wiki writes remain
    // confined to the guarded tool layer above.
    private static readonly HashSet<string> _allowedNamespacePrefixes =
    [
        "Grimoire.AgentRuntime.Guardrails",
        "Grimoire.AgentRuntime.Core.Adapters.Replay",
        // 014-wiki-storage-restructure R5: the shared, harness-side WikiLogAppender
        // backstop (same allow-listed status IngestAgentGuardedWriteBoundaryRuleTests
        // already gives it) — a direct File.AppendAllTextAsync write outside the guarded
        // tool boundary by design, exactly like the pre-014 Ingest-only IngestLogAppender
        // it generalizes; Query now shares it (new — Query had no backstop before).
        "Grimoire.AgentRuntime.WikiLog",
    ];

    // Method name substrings that indicate filesystem-write operations. Kept in sync
    // with IngestAgentGuardedWriteBoundaryRuleTests's _writeMethods list.
    private static readonly string[] _writeMethods =
    [
        "System.IO.File::WriteAllText",
        "System.IO.File::WriteAllBytes",
        "System.IO.File::WriteAllLines",
        "System.IO.File::AppendAllText",
        "System.IO.File::AppendAllLines",
        "System.IO.File::Create",
        "System.IO.File::Delete",
        "System.IO.File::Move",
        "System.IO.File::Copy",
        "System.IO.File::Replace",
        "System.IO.File::WriteAllTextAsync",
        "System.IO.File::WriteAllBytesAsync",
        "System.IO.File::WriteAllLinesAsync",
        "System.IO.File::AppendAllTextAsync",
        "System.IO.File::AppendAllLinesAsync",
        "System.IO.Directory::CreateDirectory",
        "System.IO.Directory::Delete",
        "System.IO.Directory::Move",
        "System.IO.StreamWriter::.ctor",
    ];

    [Fact]
    public void QueryAgent_FilesystemWriteAPIs_MustOnlyBeCalledFromAllowedNamespaces()
    {
        var violations = new List<string>();

        foreach (var scannedAssembly in _scannedAssemblies)
        {
            var assembly = AssemblyDefinition.ReadAssembly(scannedAssembly.Location);

            foreach (var module in assembly.Modules)
            {
                foreach (var (type, effectiveNamespace) in module.Types.SelectMany(t => GetAllTypesWithNamespace(t, t.Namespace)))
                {
                    if (string.IsNullOrEmpty(effectiveNamespace))
                        continue;

                    // Skip types in the allowed namespaces.
                    if (_allowedNamespacePrefixes.Any(ns => effectiveNamespace.StartsWith(ns, StringComparison.Ordinal)))
                        continue;

                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody)
                            continue;

                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (instruction.OpCode != OpCodes.Call &&
                                instruction.OpCode != OpCodes.Callvirt &&
                                instruction.OpCode != OpCodes.Newobj)
                                continue;

                            if (instruction.Operand is not MethodReference callee)
                                continue;

                            var callSig = $"{callee.DeclaringType.FullName}::{callee.Name}";
                            if (_writeMethods.Any(w => callSig.StartsWith(w, StringComparison.Ordinal)))
                            {
                                violations.Add($"{type.FullName}.{method.Name} [{effectiveNamespace}] → {callSig}");
                            }
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-015 (supersedes ADR-011 C7): filesystem-write APIs reachable from " +
            "Grimoire.QueryAgent must only be called from Grimoire.AgentRuntime.Guardrails " +
            "(incl. its Coordination sub-namespace). Violations:\n" + string.Join("\n", violations));
    }

    private static IEnumerable<(TypeDefinition Type, string EffectiveNamespace)> GetAllTypesWithNamespace(
        TypeDefinition type, string parentNamespace)
    {
        var ns = string.IsNullOrEmpty(type.Namespace) ? parentNamespace : type.Namespace;
        yield return (type, ns);
        foreach (var nested in type.NestedTypes.SelectMany(n => GetAllTypesWithNamespace(n, ns)))
            yield return nested;
    }
}
