using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural tripwire for ADR-014 / SC-004 (011-query-conversations): the per-turn
/// Query Run Artifact location <c>data/query-runs/</c> is retired — no production
/// assembly may compose a path into it. Uses IL-level string-literal inspection
/// (Mono.Cecil, same idiom as <see cref="RuntimePathsBoundaryRuleTests"/>) to assert
/// that no production assembly contains a string literal containing the substring
/// <c>query-runs</c>: the code that could write to the retired location must not
/// exist and cannot quietly return (research.md R6).
/// </summary>
public class RetiredQueryRunsLocationRuleTests
{
    private const string RetiredLocationLiteral = "query-runs";

    // Cutover debt — emptied by T019. The current codebase has exactly two genuine
    // source occurrences of the retired literal, both deleted by the US1 cutover task
    // (T019):
    //
    //   1. GrimoirePathOptions.DefaultQueryRunsDirName (= "query-runs"). As a C# const
    //      its literal surfaces in IL at the *use site* (compiler inlining), i.e. inside
    //      GrimoirePathResolver.Resolve — which is why the resolver type is the
    //      allowlisted entry for this occurrence.
    //   2. Program.cs's "--query-runs-dir" CLI switch mapping (top-level statements
    //      compile into the namespace-less "Program" type).
    //
    // Keyed by (assembly, type) like RuntimePathsBoundaryRuleTests' allowlist so an
    // entry can never accidentally cover a same-named type in another assembly. Once
    // T019 deletes both occurrences this set MUST become empty, making the tripwire's
    // guarantee unconditional.
    private static readonly HashSet<(string Assembly, string TypeFullName)> _cutoverDebtAllowlist =
    [
        ("Grimoire.Hub", "Grimoire.Hub.Runtime.Paths.GrimoirePathResolver"),
        ("Grimoire.Hub", "Program"),
    ];

    [Fact]
    public void ProductionAssemblies_MustNotContainRetiredQueryRunsLocationLiterals()
    {
        var violations = new List<string>();

        foreach (var assemblyPath in ProductionAssemblyPaths())
        {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            foreach (var module in assembly.Modules)
            {
                foreach (var (type, _) in module.Types.SelectMany(t => GetAllTypesWithNamespace(t, t.Namespace)))
                {
                    var assemblyName = assembly.Name.Name;
                    if (_cutoverDebtAllowlist.Contains((assemblyName, type.FullName)) ||
                        _cutoverDebtAllowlist.Contains((assemblyName, GetOutermostFullName(type))))
                        continue;

                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody)
                            continue;

                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (instruction.OpCode != OpCodes.Ldstr)
                                continue;

                            if (instruction.Operand is string literal &&
                                literal.Contains(RetiredLocationLiteral, StringComparison.Ordinal))
                            {
                                violations.Add($"{assemblyName}: {type.FullName}.{method.Name} → \"{literal}\"");
                            }
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-014 / SC-004: the query-runs location is retired — no production assembly " +
            "may contain a string literal containing \"query-runs\" (outside the documented, " +
            "temporary cutover-debt allowlist, emptied by T019). Violations:\n" +
            string.Join("\n", violations));
    }

    private static IEnumerable<string> ProductionAssemblyPaths() =>
    [
        typeof(Grimoire.Hub.HubMetrics).Assembly.Location,
        typeof(Grimoire.QueryAgent.QueryToolRegistry).Assembly.Location,
        typeof(Grimoire.IngestAgent.AgentCliOptions).Assembly.Location,
        typeof(Grimoire.AgentRuntime.Core.AgentLoop).Assembly.Location,
        typeof(Grimoire.Domain.DomainAssemblyMarker).Assembly.Location,
        typeof(Grimoire.EvalRunner.EvalRunnerAssemblyMarker).Assembly.Location,
    ];

    private static string GetOutermostFullName(TypeDefinition type)
    {
        var current = type;
        while (current.DeclaringType is not null)
            current = current.DeclaringType;
        return current.FullName;
    }

    private static IEnumerable<(TypeDefinition Type, string EffectiveNamespace)> GetAllTypesWithNamespace(
        TypeDefinition type, string? parentNamespace)
    {
        var ns = string.IsNullOrEmpty(type.Namespace) ? parentNamespace : type.Namespace;
        yield return (type, ns ?? string.Empty);
        foreach (var nested in type.NestedTypes.SelectMany(n => GetAllTypesWithNamespace(n, ns)))
            yield return nested;
    }
}
