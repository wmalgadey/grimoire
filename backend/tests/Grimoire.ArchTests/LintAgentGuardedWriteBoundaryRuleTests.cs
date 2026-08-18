using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-016 (013-lint-agent, extends ADR-015): the Lint
/// agent gains a narrow, structurally-guarded write capability (frontmatter-only updates
/// to existing pages) — the same allow-listed-namespace shape already used by
/// <see cref="IngestAgentGuardedWriteBoundaryRuleTests"/>/<see cref="QueryAgentGuardedWriteBoundaryRuleTests"/>:
/// reachable filesystem-write API calls anywhere in Grimoire.LintAgent (and the shared
/// Grimoire.AgentRuntime library it depends on) are permitted only from types in
/// Grimoire.AgentRuntime.Guardrails — which also covers the
/// Grimoire.AgentRuntime.Guardrails.Coordination sub-namespace (ADR-015/ADR-016's
/// SharedFileWriteGuard/CrossProcessFileLock), by prefix match — every other type must
/// still show zero reachable write calls. At Phase 0 (013-lint-agent T001) this rule
/// passes vacuously: Grimoire.LintAgent is still an empty project skeleton (T004) with no
/// write tool wired in at all (that is Phase 3, T019). Proven live by a Red/Green probe
/// (T002): a temporary scratch class calling File.WriteAllText directly (outside the
/// allow-list) is added, confirmed to fail the rule naming the violation, then removed
/// and confirmed green again.
/// </summary>
public class LintAgentGuardedWriteBoundaryRuleTests
{
    private static readonly System.Reflection.Assembly[] _scannedAssemblies =
    [
        typeof(Grimoire.AgentRuntime.Guardrails.ToolRegistry).Assembly,
    ];

    // Namespace prefixes permitted to use filesystem-write APIs (ADR-015/ADR-016).
    // Grimoire.AgentRuntime.Guardrails is a prefix match so it also covers the
    // Coordination sub-namespace (Grimoire.AgentRuntime.Guardrails.Coordination) without a
    // second entry. Adapters.Replay (ADR-012, shared with Ingest's/Query's rules) writes
    // only the captured turn stream to the eval runner's GRIMOIRE_MODEL_CAPTURE_PATH —
    // never wiki content; wiki writes remain confined to the guarded tool layer above.
    private static readonly HashSet<string> _allowedNamespacePrefixes =
    [
        "Grimoire.AgentRuntime.Guardrails",
        "Grimoire.AgentRuntime.Core.Adapters.Replay",
        // 025-agent-owned-log (ADR-028 BR-1): Grimoire.AgentRuntime.WikiLog was exempt
        // solely to permit the WikiLogAppender backstop's File.AppendAllTextAsync. The
        // backstop is deleted and the exemption with it — the activity log is agent-owned
        // wiki content, written only through the guarded tool layer. The namespace still
        // exists (it hosts the write-free WikiLogCoverageObserver) and must contain zero
        // filesystem-write calls.
    ];

    // Method name substrings that indicate filesystem-write operations. Kept in sync with
    // IngestAgentGuardedWriteBoundaryRuleTests's/QueryAgentGuardedWriteBoundaryRuleTests's
    // _writeMethods list.
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
    public void LintAgent_FilesystemWriteAPIs_MustOnlyBeCalledFromAllowedNamespaces()
    {
        var violations = new List<string>();

        // T004: Grimoire.LintAgent exists as a real assembly (project skeleton) as of this
        // Phase 0/1 interleaving, but — unlike Grimoire.QueryAgent/Grimoire.IngestAgent —
        // exposes no public type yet (its top-level-statement Program.cs generates only an
        // internal Program class), so it cannot be referenced via typeof(...).Assembly the
        // way the other two rules do. Loaded by simple assembly name instead: the ArchTests
        // project's ProjectReference to Grimoire.LintAgent.csproj already places the
        // assembly on this test's probing path.
        var scannedAssemblies = _scannedAssemblies
            .Append(System.Reflection.Assembly.Load("Grimoire.LintAgent"))
            .ToArray();

        foreach (var scannedAssembly in scannedAssemblies)
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
            "ADR-016 (extends ADR-015): filesystem-write APIs reachable from " +
            "Grimoire.LintAgent must only be called from Grimoire.AgentRuntime.Guardrails " +
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
