using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-021 (spec 019-fast-test-tier, FR-003/FR-004/FR-005/FR-010,
/// SC-004/SC-007 — <c>contracts/deterministic-wait-rule.md</c> is this rule's exact contract).
/// Uses the same Mono.Cecil IL-scan idiom as <see cref="RuntimePathsBoundaryRuleTests"/> to ban
/// fixed unconditional real-time waits (<c>Task.Delay</c>/<c>Thread.Sleep</c>) in the
/// deterministic-tier test assemblies, so reintroducing one is rejected by the standard
/// verification pipeline rather than relying on reviewer vigilance.
///
/// A call site is exempt when either holds:
/// (a) its containing type is <c>Grimoire.IntegrationTests.TestSupport.PollAsync</c> itself —
///     the one sanctioned poll-tick implementation (research.md R4); or
/// (b) the containing method, or its declaring type (including any outer type it is nested
///     in), carries <c>[Trait("TimingDependent", "true")]</c> — the test's own subject is
///     genuinely time-based (FR-005).
///
/// Every call site scanned here actually lives inside the compiler-generated
/// <c>MoveNext</c> state machine for an <c>async</c> method (or async lambda), not directly
/// in the source method the developer wrote the <c>[Trait]</c> on. To honor a method-level
/// exemption, this rule first builds a per-assembly map from state-machine type → the real
/// <c>[AsyncStateMachine]</c>-decorated method that owns it (via reflection over that
/// attribute, the same mechanism the C# compiler itself uses), then resolves every scanned
/// call site back through that map before checking for the trait.
///
/// This rule does not judge whether a <c>TimingDependent</c> exemption is justified — that is
/// a review-time human judgment call (contracts/deterministic-wait-rule.md "Non-goals").
/// </summary>
public class DeterministicTierNoFixedWaitRuleTests
{
    private const string PollAsyncTypeFullName = "Grimoire.IntegrationTests.TestSupport.PollAsync";

    private static readonly string[] _bannedCallees =
    [
        "System.Threading.Tasks.Task::Delay",
        "System.Threading.Thread::Sleep",
    ];

    [Fact]
    public void DeterministicTierAssemblies_MustNotContainFixedUnconditionalWaits()
    {
        var violations = new List<string>();

        foreach (var assemblyPath in ScannedAssemblyPaths())
        {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            foreach (var module in assembly.Modules)
            {
                var allTypes = module.Types.SelectMany(GetAllTypesIncludingNested).ToList();
                var stateMachineOwners = BuildStateMachineOwnerMap(allTypes);

                foreach (var type in allTypes)
                {
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody)
                            continue;

                        var (effectiveType, effectiveMethod) =
                            stateMachineOwners.TryGetValue(type.FullName, out var owner)
                                ? (owner.DeclaringType, owner)
                                : (type, method);

                        if (IsExempt(type, effectiveType, effectiveMethod))
                            continue;

                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                                continue;

                            if (instruction.Operand is not MethodReference callee)
                                continue;

                            var callSig = $"{callee.DeclaringType.FullName}::{callee.Name}";
                            if (_bannedCallees.Any(banned => callSig.StartsWith(banned, StringComparison.Ordinal)))
                            {
                                violations.Add($"{assembly.Name.Name}: {effectiveType.FullName}.{effectiveMethod.Name} → {callSig}");
                            }
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-021 / FR-003/FR-010: fixed unconditional real-time waits (Task.Delay, " +
            "Thread.Sleep) are banned in deterministic-tier test code outside " +
            $"{PollAsyncTypeFullName} and methods/types carrying " +
            "[Trait(\"TimingDependent\", \"true\")]. Violations:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Maps a compiler-generated async state-machine type's full name to the real method the
    /// C# compiler generated it for, by scanning every method for
    /// <c>[AsyncStateMachine(typeof(...))]</c> (the same attribute <c>await</c>-rewriting
    /// relies on). Covers both named async methods and async lambdas/local functions.
    /// </summary>
    private static Dictionary<string, MethodDefinition> BuildStateMachineOwnerMap(IEnumerable<TypeDefinition> allTypes)
    {
        var map = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal);

        foreach (var type in allTypes)
        {
            foreach (var method in type.Methods)
            {
                var stateMachineAttribute = method.CustomAttributes.FirstOrDefault(a =>
                    a.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute");

                if (stateMachineAttribute is null || stateMachineAttribute.ConstructorArguments.Count == 0)
                    continue;

                if (stateMachineAttribute.ConstructorArguments[0].Value is TypeReference stateMachineType)
                {
                    map[stateMachineType.FullName] = method;
                }
            }
        }

        return map;
    }

    private static bool IsExempt(TypeDefinition rawType, TypeDefinition effectiveType, MethodDefinition effectiveMethod)
    {
        if (rawType.FullName == PollAsyncTypeFullName || effectiveType.FullName == PollAsyncTypeFullName)
            return true;

        if (HasTimingDependentTrait(effectiveMethod.CustomAttributes))
            return true;

        var current = (TypeDefinition?)effectiveType;
        while (current is not null)
        {
            if (HasTimingDependentTrait(current.CustomAttributes))
                return true;

            current = current.DeclaringType;
        }

        return false;
    }

    private static bool HasTimingDependentTrait(IEnumerable<CustomAttribute> attributes) =>
        attributes.Any(attribute =>
            attribute.AttributeType.FullName == "Xunit.TraitAttribute" &&
            attribute.ConstructorArguments.Count == 2 &&
            string.Equals(attribute.ConstructorArguments[0].Value as string, "TimingDependent", StringComparison.Ordinal) &&
            string.Equals(attribute.ConstructorArguments[1].Value as string, "true", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<TypeDefinition> GetAllTypesIncludingNested(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes.SelectMany(GetAllTypesIncludingNested))
            yield return nested;
    }

    private static IEnumerable<string> ScannedAssemblyPaths() =>
    [
        typeof(Grimoire.Domain.UnitTests.RemediationActionTaskStateMachineTests).Assembly.Location,
        typeof(DeterministicTierNoFixedWaitRuleTests).Assembly.Location,
        typeof(Grimoire.IntegrationTests.IngestTaskRecordWatcherTests).Assembly.Location,
        typeof(Grimoire.AgentEvals.ReplayContractTests).Assembly.Location,
    ];
}
