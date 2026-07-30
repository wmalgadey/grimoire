using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural containment rule for ADR-015: types under
/// <c>Grimoire.AgentRuntime.Guardrails.Coordination</c> (<c>SharedFileWriteGuard</c>,
/// <c>CrossProcessFileLock</c>) are the cross-process write-coordination mechanism that
/// protects <c>index.md</c>/<c>log.md</c>/existing pages from lost updates once Query and
/// Ingest can both write. Per Constitution Principle I, this is a persistence/local-
/// filesystem mechanism — port-exempt, but containment-bound: it may be constructed only
/// from <c>Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor</c> (or other types
/// within the <c>Grimoire.AgentRuntime.Guardrails</c> namespace itself, including its own
/// <c>Coordination</c> sub-namespace) — no agent host composition root or any other
/// namespace may construct these types directly (ADR-010/ADR-013 namespace-containment
/// idiom, same shape as <see cref="AgentHostModelCompositionContainmentRuleTests"/>'s D2
/// rule).
///
/// At Phase 0 (012-query-synthesis-writes T002) the Coordination namespace does not exist
/// yet (introduced by T014), so this rule passes vacuously — no construction site can
/// possibly match a namespace that has no types. Proven live by a Red/Green probe (T004):
/// a temporary scratch type under the Coordination namespace, constructed from a scratch
/// call site outside Grimoire.AgentRuntime.Guardrails (e.g. in Grimoire.QueryAgent), is
/// added, confirmed to fail the rule naming the offending call site, then both scratch
/// types are removed and the rule is confirmed green again.
/// </summary>
public class GuardrailsCoordinationContainmentRuleTests
{
    private const string CoordinationNamespace = "Grimoire.AgentRuntime.Guardrails.Coordination";
    private const string GuardrailsNamespace = "Grimoire.AgentRuntime.Guardrails";

    [Fact]
    public void AgentHostAssemblies_MustNotConstructCoordinationTypesDirectly()
    {
        var violations = new List<string>();

        foreach (var assemblyPath in ArchScan.AgentHostAssemblyPaths())
        {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            violations.AddRange(FindCoordinationConstructionSites(assembly, allowedCallerNamespacePrefix: null)
                .Select(site => $"{assembly.Name.Name}: {site}"));
        }

        Assert.True(
            violations.Count == 0,
            "ADR-015: agent host assemblies must not construct " +
            "Grimoire.AgentRuntime.Guardrails.Coordination types directly — construction is " +
            "confined to Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor (or the " +
            "Coordination namespace itself). Violations:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void AgentRuntime_CoordinationConstruction_OnlyFromGuardrailsNamespace()
    {
        var assemblyPath = System.Reflection.Assembly.Load("Grimoire.AgentRuntime").Location;
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        var violations = FindCoordinationConstructionSites(assembly, allowedCallerNamespacePrefix: GuardrailsNamespace);

        Assert.True(
            violations.Count == 0,
            "ADR-015: within Grimoire.AgentRuntime, Coordination types may be constructed " +
            "only from types in Grimoire.AgentRuntime.Guardrails (incl. Coordination " +
            "itself). Violations:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Finds every newobj/call site whose callee constructs a type under
    /// <see cref="CoordinationNamespace"/>, from a caller whose effective namespace does
    /// NOT start with <paramref name="allowedCallerNamespacePrefix"/> (or from any caller
    /// at all when <paramref name="allowedCallerNamespacePrefix"/> is null — used for the
    /// "agent hosts must never construct these" fact, where no caller in that assembly is
    /// ever allowed).
    /// </summary>
    private static List<string> FindCoordinationConstructionSites(
        AssemblyDefinition assembly, string? allowedCallerNamespacePrefix)
    {
        var violations = new List<string>();

        foreach (var module in assembly.Modules)
        {
            foreach (var (type, effectiveNamespace) in GetAllTypesWithNamespace(module.Types, null))
            {
                if (allowedCallerNamespacePrefix is not null &&
                    effectiveNamespace.StartsWith(allowedCallerNamespacePrefix, StringComparison.Ordinal))
                    continue;

                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode != OpCodes.Newobj &&
                            instruction.OpCode != OpCodes.Call &&
                            instruction.OpCode != OpCodes.Callvirt)
                            continue;

                        if (instruction.Operand is not MethodReference callee)
                            continue;

                        if (callee.Name != ".ctor")
                            continue;

                        var calleeNamespace = callee.DeclaringType.GetElementType().Namespace;
                        if (string.IsNullOrEmpty(calleeNamespace) ||
                            !calleeNamespace.StartsWith(CoordinationNamespace, StringComparison.Ordinal))
                            continue;

                        violations.Add(
                            $"{type.FullName}.{method.Name} [{effectiveNamespace}] → " +
                            $"{callee.DeclaringType.FullName}::{callee.Name}");
                    }
                }
            }
        }

        return violations;
    }

    private static IEnumerable<(TypeDefinition Type, string EffectiveNamespace)> GetAllTypesWithNamespace(
        IEnumerable<TypeDefinition> types, string? parentNamespace)
    {
        foreach (var type in types)
        {
            var ns = string.IsNullOrEmpty(type.Namespace) ? (parentNamespace ?? string.Empty) : type.Namespace;
            yield return (type, ns);
            foreach (var nested in GetAllTypesWithNamespace(type.NestedTypes, ns))
                yield return nested;
        }
    }
}
