using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule R3 for ADR-022: no production type outside
/// <c>Grimoire.Hub.Runtime.Paths</c> may reference an agent-instruction filename literal
/// (<c>system-prompt.md</c>, <c>default-user-prompt.md</c>, <c>policy.json</c>) as a
/// write target. The hub composes instruction *paths*; it must never author instruction
/// *content* (Constitution Principle V).
///
/// Precise write-target dataflow analysis is out of scope for an IL tripwire; instead
/// this rule flags any method body — outside the allowed namespace — that contains BOTH
/// one of the forbidden literals AND a call to a known file-write API. That is a
/// deliberately coarse heuristic (a method could reference the literal for an unrelated,
/// read-only reason while also happening to write some other file), but it is exactly
/// the shape a Principle V violation would take, and the allowed-namespace exemption
/// keeps the legitimate path-composition code — which only ever *reads* these documents
/// — out of scope entirely.
/// </summary>
public class InstructionAuthorshipBoundaryRuleTests
{
    private const string AllowedNamespacePrefix = "Grimoire.Hub.Runtime.Paths";

    private static readonly string[] _instructionFilenameLiterals =
    [
        "system-prompt.md",
        "default-user-prompt.md",
        "policy.json",
    ];

    private static readonly string[] _writeApiPrefixes =
    [
        "System.IO.File::WriteAllText",
        "System.IO.File::WriteAllBytes",
        "System.IO.File::WriteAllLines",
        "System.IO.File::AppendAllText",
        "System.IO.File::Create",
        "System.IO.StreamWriter::.ctor",
    ];

    [Fact]
    public void ProductionTypes_OutsideRuntimePaths_MustNotWriteInstructionFilenames()
    {
        var violations = new List<string>();

        foreach (var assemblyPath in ProductionAssemblyPaths())
        {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            foreach (var module in assembly.Modules)
            {
                foreach (var (type, effectiveNamespace) in module.Types.SelectMany(t => FlattenTypesWithNamespace(t, t.Namespace)))
                {
                    if (effectiveNamespace.StartsWith(AllowedNamespacePrefix, StringComparison.Ordinal))
                        continue;

                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody)
                            continue;

                        var referencedLiterals = new List<string>();
                        var callsWriteApi = false;

                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (instruction.OpCode == OpCodes.Ldstr &&
                                instruction.Operand is string literal &&
                                _instructionFilenameLiterals.Contains(literal))
                            {
                                referencedLiterals.Add(literal);
                            }

                            if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Newobj) &&
                                instruction.Operand is MethodReference callee)
                            {
                                var callSig = $"{callee.DeclaringType.FullName}::{callee.Name}";
                                if (_writeApiPrefixes.Any(p => callSig.StartsWith(p, StringComparison.Ordinal)))
                                    callsWriteApi = true;
                            }
                        }

                        if (referencedLiterals.Count > 0 && callsWriteApi)
                        {
                            violations.Add(
                                $"{assembly.Name.Name}: {type.FullName}.{method.Name} references " +
                                $"[{string.Join(", ", referencedLiterals.Distinct())}] and calls a file-write API");
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-022 rule R3: no production type outside Grimoire.Hub.Runtime.Paths may " +
            "write an instruction filename (Constitution Principle V — the hub composes " +
            "instruction paths, never instruction content). Violations:\n" + string.Join("\n", violations));
    }

    private static IEnumerable<string> ProductionAssemblyPaths() =>
    [
        typeof(Grimoire.Hub.HubMetrics).Assembly.Location,
        typeof(Grimoire.IngestAgent.IngestCliOptions).Assembly.Location,
        typeof(Grimoire.QueryAgent.QueryCliOptions).Assembly.Location,
        typeof(Grimoire.LintAgent.LintCliOptions).Assembly.Location,
        typeof(Grimoire.Domain.DomainAssemblyMarker).Assembly.Location,
        typeof(Grimoire.EvalRunner.Workspace.EvalPaths).Assembly.Location,
    ];

    private static IEnumerable<(TypeDefinition Type, string EffectiveNamespace)> FlattenTypesWithNamespace(
        TypeDefinition type, string? parentNamespace)
    {
        var ns = string.IsNullOrEmpty(type.Namespace) ? parentNamespace : type.Namespace;
        yield return (type, ns ?? string.Empty);
        foreach (var nested in type.NestedTypes.SelectMany(n => FlattenTypesWithNamespace(n, ns)))
            yield return nested;
    }
}
