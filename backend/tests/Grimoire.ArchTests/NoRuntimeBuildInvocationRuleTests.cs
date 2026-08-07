using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule R4 for ADR-022: no production assembly may contain the IL
/// string literals that constitute a build invocation — <c>.csproj</c>, <c>--project</c>,
/// <c>msbuild</c>, or a bare <c>build</c>/<c>restore</c> token (a <c>dotnet</c> CLI
/// subcommand argument). The hub must consume build artifacts and never produce them
/// (ADR-022 "the hub consumes build artifacts and never produces them"): a running hub
/// that can trigger a restore/compile inside a request has unbounded agent-start latency
/// and surfaces compile errors as agent-run failures.
///
/// <see cref="Grimoire.EvalRunner"/>'s diagnostic messages mention "dotnet build" in
/// prose (e.g. "Build first: dotnet build backend/Grimoire.slnx") without invoking it;
/// those are one combined string literal, not a standalone "build"/"restore" token, so
/// they do not match this rule's exact-literal comparison and need no exemption today.
/// <see cref="_knownMessageOnlyLiterals"/> exists as the documented, typed exemption
/// point ADR-022 calls for, should a future message ever need one — it is empty because
/// none currently does.
/// </summary>
public class NoRuntimeBuildInvocationRuleTests
{
    private static readonly string[] _buildInvocationLiterals = [".csproj", "--project", "msbuild", "build", "restore"];

    // Documented, typed allow-list for message-only literals (ADR-022 rule R4). Keyed by
    // (assembly, type, literal) so an exemption can never accidentally cover an unrelated
    // type. Empty today — see class remarks.
    private static readonly HashSet<(string Assembly, string TypeFullName, string Literal)> _knownMessageOnlyLiterals = [];

    [Fact]
    public void ProductionAssemblies_MustNotContainBuildInvocationLiterals()
    {
        var violations = new List<string>();

        foreach (var assemblyPath in ProductionAssemblyPaths())
        {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            var assemblyName = assembly.Name.Name;

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

                            if (instruction.Operand is not string literal || !_buildInvocationLiterals.Contains(literal))
                                continue;

                            if (_knownMessageOnlyLiterals.Contains((assemblyName, type.FullName, literal)))
                                continue;

                            violations.Add($"{assemblyName}: {type.FullName}.{method.Name} → \"{literal}\"");
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-022 rule R4: no production assembly may contain a build-invocation literal " +
            "(.csproj, --project, msbuild, build, restore) — the hub must launch pre-built " +
            "agent artifacts only, never build them. Violations:\n" + string.Join("\n", violations));
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

    private static IEnumerable<TypeDefinition> FlattenTypes(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes.SelectMany(FlattenTypes))
            yield return nested;
    }
}
