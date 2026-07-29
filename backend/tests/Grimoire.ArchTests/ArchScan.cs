using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Shared Mono.Cecil scan helpers for the ADR-013 rules (D1/D2). Same IL-scan idiom as
/// IngestAgentGuardedWriteBoundaryRuleTests; extracted because the duplication-containment rules
/// share the call-site walk.
/// </summary>
internal static class ArchScan
{
    internal sealed record CallSite(
        string TopLevelTypeFullName,
        string EffectiveNamespace,
        string Description);

    /// <summary>
    /// Agent host assemblies, discovered by naming pattern (Grimoire.*Agent) from the
    /// test output directory so any future agent host (e.g. Grimoire.LintAgent) is
    /// covered the moment it is referenced by the test projects — without editing this
    /// rule.
    /// </summary>
    internal static IEnumerable<string> AgentHostAssemblyPaths()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var paths = Directory.GetFiles(baseDirectory, "Grimoire.*Agent.dll").OrderBy(p => p, StringComparer.Ordinal).ToList();

        // The two known hosts must be present — an empty scan would pass vacuously.
        Assert.Contains(paths, p => Path.GetFileName(p) == "Grimoire.IngestAgent.dll");
        Assert.Contains(paths, p => Path.GetFileName(p) == "Grimoire.QueryAgent.dll");

        return paths;
    }

    /// <summary>
    /// All call/newobj sites in the assembly whose callee matches one of the
    /// "DeclaringTypeFullName::MethodName" prefixes.
    /// </summary>
    internal static IEnumerable<CallSite> FindCalls(AssemblyDefinition assembly, string[] calleePrefixes)
        => FindSites(assembly, (callee, _) =>
            calleePrefixes.Any(p => $"{callee.DeclaringType.FullName}::{callee.Name}".StartsWith(p, StringComparison.Ordinal)));

    /// <summary>
    /// All constructor references (newobj, or any direct .ctor call) in the assembly
    /// whose constructed type is one of the given full names.
    /// </summary>
    internal static IEnumerable<CallSite> FindConstructions(AssemblyDefinition assembly, string[] constructedTypeFullNames)
        => FindSites(assembly, (callee, _) =>
            callee.Name == ".ctor" &&
            constructedTypeFullNames.Contains(callee.DeclaringType.GetElementType().FullName, StringComparer.Ordinal));

    private static IEnumerable<CallSite> FindSites(
        AssemblyDefinition assembly,
        Func<MethodReference, Instruction, bool> matches)
    {
        foreach (var module in assembly.Modules)
        {
            foreach (var (type, topLevel, effectiveNamespace) in module.Types.SelectMany(t => WithTopLevel(t, t, t.Namespace)))
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

                        if (matches(callee, instruction))
                        {
                            yield return new CallSite(
                                topLevel.FullName,
                                effectiveNamespace,
                                $"{type.FullName}.{method.Name} [{effectiveNamespace}] → {callee.DeclaringType.FullName}::{callee.Name}");
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<(TypeDefinition Type, TypeDefinition TopLevel, string EffectiveNamespace)> WithTopLevel(
        TypeDefinition type, TypeDefinition topLevel, string parentNamespace)
    {
        // Nested types (async state machines, closures) inherit the top-level type's
        // namespace and identity for baseline matching.
        var ns = string.IsNullOrEmpty(type.Namespace) ? parentNamespace : type.Namespace;
        yield return (type, topLevel, ns);
        foreach (var nested in type.NestedTypes.SelectMany(n => WithTopLevel(n, topLevel, ns)))
            yield return nested;
    }
}
