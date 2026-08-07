using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule R2 for ADR-022: no production assembly may contain an IL
/// string literal equal to a configured root's default value (<c>.grimoire</c>,
/// <c>llm-wiki</c>) — the defaults exist only in <c>appsettings.json</c> (FR-005). Same
/// tripwire idiom as ADR-009's <c>rev-parse</c> literal scan
/// (<see cref="RuntimePathsBoundaryRuleTests"/>): a code-level default is precisely what
/// let the old surface feel simultaneously mandatory and invisible.
/// </summary>
public class NoCodeLevelPathDefaultsRuleTests
{
    private static readonly string[] _forbiddenDefaultLiterals = [".grimoire", "llm-wiki"];

    [Fact]
    public void ProductionAssemblies_MustNotContainCodeLevelRootDefaults()
    {
        var violations = new List<string>();

        foreach (var assemblyPath in ProductionAssemblyPaths())
        {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.Types.SelectMany(FlattenTypes))
                {
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody)
                            continue;

                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (instruction.OpCode != OpCodes.Ldstr)
                                continue;

                            if (instruction.Operand is string literal && _forbiddenDefaultLiterals.Contains(literal))
                            {
                                violations.Add($"{assembly.Name.Name}: {type.FullName}.{method.Name} → \"{literal}\"");
                            }
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-022 rule R2: no production assembly may contain a code-level default for " +
            "a root directory (.grimoire, llm-wiki) — these values must exist only in " +
            "appsettings.json (FR-005). Violations:\n" + string.Join("\n", violations));
    }

    private static IEnumerable<string> ProductionAssemblyPaths() =>
    [
        typeof(Grimoire.Hub.HubMetrics).Assembly.Location,
        typeof(Grimoire.IngestAgent.IngestCliOptions).Assembly.Location,
        typeof(Grimoire.QueryAgent.QueryCliOptions).Assembly.Location,
        typeof(Grimoire.LintAgent.LintCliOptions).Assembly.Location,
        typeof(Grimoire.Domain.DomainAssemblyMarker).Assembly.Location,
    ];

    private static IEnumerable<TypeDefinition> FlattenTypes(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes.SelectMany(FlattenTypes))
            yield return nested;
    }
}
