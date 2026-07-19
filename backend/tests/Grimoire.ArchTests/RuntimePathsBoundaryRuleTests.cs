using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-009: every runtime location is composed in exactly
/// one place. Uses IL-level inspection (Mono.Cecil, same idiom as
/// GuardedWriteBoundaryRuleTests) to detect (a) ambient-directory API usage outside the
/// single composition point and (b) any remaining git-repo-root discovery.
/// </summary>
public class RuntimePathsBoundaryRuleTests
{
    // The only namespace permitted to call ambient-directory APIs (ADR-009: single
    // composition point).
    private const string AllowedNamespacePrefix = "Grimoire.Hub.Runtime.Paths";

    // No known violations remain (T009/T010 cleaned up Grimoire.Hub, T012 cleaned up
    // Grimoire.IngestAgent). Kept as a typed, empty allowlist — keyed by (assembly, type),
    // not type name alone — so a future documented exception can never accidentally cover
    // a same-named type in a different assembly (e.g. both processes compile a top-level
    // "Program" type).
    private static readonly HashSet<(string Assembly, string TypeFullName)> _knownViolationTypes = [];

    private static readonly string[] _ambientDirectoryMethods =
    [
        "System.IO.Directory::GetCurrentDirectory",
        "System.Environment::get_CurrentDirectory",
        "System.AppContext::get_BaseDirectory",
    ];

    private static readonly string[] _repoDiscoveryLiterals =
    [
        "rev-parse",
        "--show-toplevel",
    ];

    [Fact]
    public void AmbientDirectoryApis_MustOnlyBeCalledFromRuntimePaths()
    {
        var violations = new List<string>();

        foreach (var assemblyPath in ProductionAssemblyPaths())
        {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            foreach (var module in assembly.Modules)
            {
                foreach (var (type, effectiveNamespace) in module.Types.SelectMany(t => GetAllTypesWithNamespace(t, t.Namespace)))
                {
                    if (effectiveNamespace.StartsWith(AllowedNamespacePrefix, StringComparison.Ordinal))
                        continue;

                    var assemblyName = assembly.Name.Name;
                    if (_knownViolationTypes.Contains((assemblyName, type.FullName)) ||
                        _knownViolationTypes.Contains((assemblyName, GetOutermostFullName(type))))
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
                            if (_ambientDirectoryMethods.Any(m => callSig.StartsWith(m, StringComparison.Ordinal)))
                            {
                                violations.Add($"{assembly.Name.Name}: {type.FullName}.{method.Name} → {callSig}");
                            }
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-009: ambient-directory APIs (Directory.GetCurrentDirectory, " +
            "Environment.CurrentDirectory, AppContext.BaseDirectory) must only be called " +
            "from Grimoire.Hub.Runtime.Paths (the single path-composition point). Violations:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void ProductionAssemblies_MustNotContainGitRepoDiscoveryLiterals()
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
                    if (_knownViolationTypes.Contains((assemblyName, type.FullName)) ||
                        _knownViolationTypes.Contains((assemblyName, GetOutermostFullName(type))))
                        continue;

                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody)
                            continue;

                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (instruction.OpCode != OpCodes.Ldstr)
                                continue;

                            if (instruction.Operand is string literal && _repoDiscoveryLiterals.Contains(literal))
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
            "ADR-009 / FR-002: no production assembly may invoke git repo-root discovery " +
            "(rev-parse --show-toplevel). Violations:\n" + string.Join("\n", violations));
    }

    private static IEnumerable<string> ProductionAssemblyPaths() =>
    [
        typeof(Grimoire.Hub.HubMetrics).Assembly.Location,
        typeof(Grimoire.IngestAgent.AgentCliOptions).Assembly.Location,
        typeof(Grimoire.Domain.DomainAssemblyMarker).Assembly.Location,
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
