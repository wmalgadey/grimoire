using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule M3 for ADR-024: no production assembly may contain an IL
/// string literal beginning with <c>[[tasks/</c>, <c>[[conversations/</c>,
/// <c>[[findings/</c>, or <c>[[remediation-tasks/</c> — a wiki-relative link into a
/// harness-record folder is dangling by construction once those folders anchor at
/// <c>MemoryDir</c> instead of <c>WikiDir</c> (research R3). Same tripwire idiom as
/// ADR-022 rule R2 / ADR-009's <c>rev-parse</c> literal scan
/// (<see cref="NoCodeLevelPathDefaultsRuleTests"/>, <see cref="RuntimePathsBoundaryRuleTests"/>).
/// </summary>
public class NoWikiRelativeHarnessRecordLinkRuleTests
{
    private static readonly string[] _forbiddenLinkPrefixes =
        ["[[tasks/", "[[conversations/", "[[findings/", "[[remediation-tasks/"];

    [Fact]
    public void ProductionAssemblies_MustNotContainWikiRelativeHarnessRecordLinks()
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

                            if (instruction.Operand is string literal &&
                                _forbiddenLinkPrefixes.Any(prefix => literal.Contains(prefix, StringComparison.Ordinal)))
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
            "ADR-024 rule M3: no production assembly may contain a wiki-relative link into a " +
            "harness-record folder ([[tasks/, [[conversations/, [[findings/, [[remediation-tasks/) " +
            "— these folders anchor outside the wiki tree at MemoryDir, so such a link is dangling " +
            "by construction. Violations:\n" + string.Join("\n", violations));
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
