using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-031 R3 (026-guarded-tool-surface, FR-022): deletion is a
/// distinct, explicitly granted capability, never acquired by inheritance. Reachable
/// filesystem-deletion API calls anywhere in any agent host (Grimoire.IngestAgent,
/// Grimoire.QueryAgent, Grimoire.LintAgent) or the shared Grimoire.AgentRuntime library they
/// depend on are permitted only from types in Grimoire.AgentRuntime.Guardrails — which also
/// covers the Grimoire.AgentRuntime.Guardrails.Coordination sub-namespace, by prefix match —
/// or from Grimoire.AgentRuntime.Core.Adapters.Replay, the harness-record namespace that
/// writes ADR-012's captured turn stream and needs no exemption from this rule in practice
/// but is kept consistent with the sibling write-boundary rules
/// (<see cref="LintAgentGuardedWriteBoundaryRuleTests"/>,
/// <see cref="QueryAgentGuardedWriteBoundaryRuleTests"/>,
/// <see cref="IngestAgentGuardedWriteBoundaryRuleTests"/>) that already carry the same
/// exemption for the same reason. Every other type must show zero reachable deletion calls.
///
/// This rule is narrower than the sibling write-boundary rules on purpose: those already
/// cover <c>File.Delete</c>/<c>Directory.Delete</c> among a wider set of write methods, but
/// none of them enumerates every agent host in one place, and none of them exists solely to
/// protect the one action ADR-031 singles out as unrecoverable. Deletion is asserted here on
/// its own so a future write-boundary rule change cannot silently drop delete coverage.
///
/// At Phase 0 (026-guarded-tool-surface T001) this rule passes vacuously: no `delete_file`
/// tool is wired into any dispatch path yet (that is Phase 4, T033). Proven live by a
/// Red/Green probe (recorded in the T001 commit message): a temporary scratch class calling
/// <c>File.Delete</c> directly from Grimoire.LintAgent (outside the allow-list) is added,
/// confirmed to fail the rule naming the violation, then removed and confirmed green again.
/// </summary>
public class WikiDeletionGuardedBoundaryRuleTests
{
    // Namespace prefixes permitted to reach filesystem-deletion APIs (ADR-031 R3).
    // Grimoire.AgentRuntime.Guardrails is a prefix match so it also covers the Coordination
    // sub-namespace without a second entry.
    private static readonly HashSet<string> _allowedNamespacePrefixes =
    [
        "Grimoire.AgentRuntime.Guardrails",
        "Grimoire.AgentRuntime.Core.Adapters.Replay",
    ];

    // Filesystem APIs that remove content from disk. A narrow list, deliberately smaller
    // than the sibling write-boundary rules' _writeMethods — this rule protects exactly the
    // one action ADR-031 R3 calls out, not every write.
    private static readonly string[] _deleteMethods =
    [
        "System.IO.File::Delete",
        "System.IO.Directory::Delete",
        "System.IO.FileInfo::Delete",
        "System.IO.DirectoryInfo::Delete",
        "System.IO.FileSystemInfo::Delete",
    ];

    [Fact]
    public void WikiDeletion_FilesystemAPIs_MustOnlyBeCalledFromAllowedNamespaces()
    {
        var violations = new List<string>();

        foreach (var assemblyPath in ScannedAssemblyPaths())
        {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

            foreach (var module in assembly.Modules)
            {
                foreach (var (type, effectiveNamespace) in module.Types.SelectMany(t => GetAllTypesWithNamespace(t, t.Namespace)))
                {
                    if (string.IsNullOrEmpty(effectiveNamespace))
                        continue;

                    if (_allowedNamespacePrefixes.Any(ns => effectiveNamespace.StartsWith(ns, StringComparison.Ordinal)))
                        continue;

                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody)
                            continue;

                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                                continue;

                            if (instruction.Operand is not MethodReference callee)
                                continue;

                            var callSig = $"{callee.DeclaringType.FullName}::{callee.Name}";
                            if (_deleteMethods.Any(w => callSig.StartsWith(w, StringComparison.Ordinal)))
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
            "ADR-031 R3: filesystem-deletion APIs (File.Delete/Directory.Delete) must only be " +
            "called from Grimoire.AgentRuntime.Guardrails (incl. its Coordination sub-namespace) " +
            "or Grimoire.AgentRuntime.Core.Adapters.Replay. Violations:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Every agent host, discovered by naming pattern so a future host is covered without
    /// editing this rule, plus the shared runtime library those hosts all depend on.
    /// </summary>
    private static IEnumerable<string> ScannedAssemblyPaths()
    {
        var agentRuntimePath = typeof(Grimoire.AgentRuntime.Guardrails.ToolRegistry).Assembly.Location;
        return ArchScan.AgentHostAssemblyPaths().Append(agentRuntimePath);
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
