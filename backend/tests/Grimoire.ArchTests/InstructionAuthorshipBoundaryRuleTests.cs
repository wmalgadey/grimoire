using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule R3 for ADR-022, extended by ADR-053: no production type may
/// reference an agent-instruction filename literal (<c>system-prompt.md</c>,
/// <c>default-user-prompt.md</c>, <c>policy.json</c>, <c>foundation-prompt.md</c>) as a
/// write target, except where a narrow allow-list says exactly which literal(s) that
/// namespace may legitimately touch. Nothing may *author* instruction content:
/// <c>Grimoire.Hub.Runtime.Paths</c> is exempt for every literal (it only ever composes
/// paths, never writes); <c>Grimoire.Hub.WikiIdentity</c> — the wiki-identity wizard's
/// custodian — is exempt for <c>foundation-prompt.md</c> only, since ADR-053's Boundary
/// Rule and FR-019 both say the wizard persists bytes received whole for that one
/// document and nothing else. A WikiIdentity type that also referenced, say,
/// <c>system-prompt.md</c> alongside a write call would still be flagged: the exemption
/// is per-literal, not per-namespace, so it cannot silently widen to cover a future
/// Principle V violation in that namespace (Constitution Principle V).
///
/// Precise write-target dataflow analysis is out of scope for an IL tripwire; instead
/// this rule flags any method body that contains BOTH one of the forbidden literals AND
/// a call to a known file-write API, unless every referenced literal is one that
/// method's namespace is allow-listed for. That is a deliberately coarse heuristic (a
/// method could reference the literal for an unrelated, read-only reason while also
/// happening to write some other file), but it is exactly the shape a Principle V
/// violation would take.
/// </summary>
public class InstructionAuthorshipBoundaryRuleTests
{
    private const string PathsNamespacePrefix = "Grimoire.Hub.Runtime.Paths";
    private const string WikiIdentityNamespacePrefix = "Grimoire.Hub.WikiIdentity";

    /// <summary>
    /// The only instruction-filename literal <see cref="WikiIdentityNamespacePrefix"/> may
    /// legitimately reference alongside a file-write call — the document the wizard
    /// custodies (ADR-053's Boundary Rule, FR-019). Referencing any other literal there
    /// still trips the rule.
    /// </summary>
    private const string WikiIdentityAllowedLiteral = "foundation-prompt.md";

    private static readonly string[] _instructionFilenameLiterals =
    [
        "system-prompt.md",
        "default-user-prompt.md",
        "policy.json",
        "foundation-prompt.md",
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
                    if (effectiveNamespace.StartsWith(PathsNamespacePrefix, StringComparison.Ordinal))
                        continue;

                    string[] allowedLiteralsForNamespace = effectiveNamespace.StartsWith(WikiIdentityNamespacePrefix, StringComparison.Ordinal)
                        ? [WikiIdentityAllowedLiteral]
                        : [];

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

                        var unallowedLiterals = referencedLiterals.Distinct().Except(allowedLiteralsForNamespace).ToList();
                        if (unallowedLiterals.Count > 0 && callsWriteApi)
                        {
                            violations.Add(
                                $"{assembly.Name.Name}: {type.FullName}.{method.Name} references " +
                                $"[{string.Join(", ", unallowedLiterals)}] and calls a file-write API");
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-022 rule R3 (extended by ADR-053): no production type may write an " +
            "instruction filename it isn't allow-listed for — Grimoire.Hub.Runtime.Paths " +
            "for every literal, Grimoire.Hub.WikiIdentity for foundation-prompt.md only " +
            "(Constitution Principle V — the hub composes instruction paths and persists " +
            "custodied bytes, never authors instruction content). Violations:\n" + string.Join("\n", violations));
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
