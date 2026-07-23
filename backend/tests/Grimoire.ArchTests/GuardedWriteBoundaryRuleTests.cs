using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-006: filesystem-WRITE operations within
/// Grimoire.IngestAgent are only permitted in the three guarded namespaces.
/// Uses IL-level inspection (Mono.Cecil) to distinguish write calls from reads.
/// </summary>
public class GuardedWriteBoundaryRuleTests
{
    // Namespaces inside Grimoire.IngestAgent that are permitted to use filesystem-write APIs.
    // Adapters.Replay (ADR-011) writes only the captured turn stream to the eval runner's
    // GRIMOIRE_MODEL_CAPTURE_PATH — never wiki content; wiki writes remain confined to the
    // guarded tool layer above.
    private static readonly HashSet<string> _allowedNamespacePrefixes =
    [
        "Grimoire.IngestAgent.Guardrails",
        "Grimoire.IngestAgent.TaskArtifact",
        "Grimoire.IngestAgent.IngestLog",
        "Grimoire.IngestAgent.AgentCore.Adapters.Replay",
    ];

    // Method name substrings that indicate filesystem-write operations.
    // Matched against the declaring type + method name from IL call instructions.
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
    public void IngestAgent_FilesystemWriteAPIs_MustOnlyBeCalledFromAllowedNamespaces()
    {
        var assemblyPath = typeof(Grimoire.IngestAgent.AgentCliOptions).Assembly.Location;
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        var violations = new List<string>();

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

        Assert.True(
            violations.Count == 0,
            $"Filesystem-write APIs must only be called from allowed namespaces " +
            $"(Guardrails, TaskArtifact, IngestLog). Violations:\n" +
            string.Join("\n", violations));
    }

    private static IEnumerable<(TypeDefinition Type, string EffectiveNamespace)> GetAllTypesWithNamespace(
        TypeDefinition type, string parentNamespace)
    {
        // Nested types (e.g., async state machines) inherit the declaring type's namespace.
        var ns = string.IsNullOrEmpty(type.Namespace) ? parentNamespace : type.Namespace;
        yield return (type, ns);
        foreach (var nested in type.NestedTypes.SelectMany(n => GetAllTypesWithNamespace(n, ns)))
            yield return nested;
    }
}
