using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-011 (C7): the Query agent has no write capability at
/// all (FR-011, FR-014) — unlike Grimoire.IngestAgent's GuardedWriteBoundaryRuleTests,
/// which allows filesystem-write APIs in three designated namespaces, this rule allows
/// none: zero filesystem-write API calls may be reachable anywhere in the
/// Grimoire.QueryAgent assembly, full stop. Proven live by a Red/Green probe (T004): a
/// temporary scratch class calling File.WriteAllText is added, the rule is confirmed to
/// fail, then the scratch class is removed and the rule is confirmed to pass again.
/// </summary>
public class QueryAgentGuardedWriteBoundaryRuleTests
{
    // Method name substrings that indicate filesystem-write operations. Kept in sync
    // with GuardedWriteBoundaryRuleTests's _writeMethods list.
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
    public void QueryAgent_FilesystemWriteAPIs_MustNeverBeReachable()
    {
        // Loaded by name (not typeof) so this rule compiles regardless of which types
        // Grimoire.QueryAgent currently declares.
        var assemblyPath = System.Reflection.Assembly.Load("Grimoire.QueryAgent").Location;
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        var violations = new List<string>();

        foreach (var module in assembly.Modules)
        {
            foreach (var (type, effectiveNamespace) in module.Types.SelectMany(t => GetAllTypesWithNamespace(t, t.Namespace)))
            {
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

        Assert.True(
            violations.Count == 0,
            "C7 (ADR-011): the Grimoire.QueryAgent assembly must contain zero reachable " +
            "filesystem-write API calls (no allowed namespace — the agent has no write " +
            "capability at all). Violations:\n" + string.Join("\n", violations));
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
