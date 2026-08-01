using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-018 (015-lint-board-parity T002, extended T042): the
/// authorization gate is a dispatch <em>precondition</em> — the only code path that can
/// spawn a remediation-<b>execution</b> agent process is
/// <c>Grimoire.Hub.RemediationTasks.RemediationRunCoordinator.TryStartNextAsync</c>,
/// which dequeues exclusively <c>Authorized</c> rows under the slot lock (SC-005/FR-008).
/// Enforced with the allow-listed-caller shape (same idiom as
/// <see cref="GuardrailsCoordinationContainmentRuleTests"/>): within
/// <c>Grimoire.Hub.RemediationTasks</c>, only the allow-listed types below may reference
/// the <c>Grimoire.Hub.AgentDispatch.IAgentProcessLauncher</c> port — an unauthorized
/// execution would require a call site this rule proves does not exist.
///
/// T042 (US5, FR-012) extends the allow-list with
/// <c>RemediationMessageTurnCoordinator</c>: a message turn is advisory Q&amp;A about a
/// <c>Proposed</c> task, spawned via a distinct <c>IAgentProcessLauncher</c> overload
/// (<c>RemediationMessageTurnAgentRequest</c>, a deny-by-default no-write policy) that
/// never transitions <c>RemediationActionTask</c>'s execution state machine and applies
/// no wiki write — so a second, independently allow-listed call site carries none of
/// SC-005's risk (that guarantee is specifically "no <em>execution</em> without prior
/// authorization"). This is a deliberate, documented extension, not a general opening of
/// the namespace: exactly these two types, for exactly their own overloads.
///
/// At Phase 0 (015-lint-board-parity T002) the RemediationTasks namespace does not exist
/// yet, so the rule passes vacuously. Proven live by a Red/Green probe (a temporary class
/// in the remediation namespace holding an <c>IAgentProcessLauncher</c> field, confirmed
/// to fail the rule naming the offending type, then deleted; result documented in the
/// Phase 0 commit message).
/// </summary>
public class RemediationExecutionDispatchRuleTests
{
    private const string RemediationTasksNamespace = "Grimoire.Hub.RemediationTasks";
    private const string LauncherPortFullName = "Grimoire.Hub.AgentDispatch.IAgentProcessLauncher";

    private static readonly HashSet<string> _allowedCallerFullNames =
    [
        "Grimoire.Hub.RemediationTasks.RemediationRunCoordinator",
        // T042 (US5, FR-012): see the class doc comment above for why this second call
        // site does not weaken SC-005.
        "Grimoire.Hub.RemediationTasks.RemediationMessageTurnCoordinator",
    ];

    [Fact]
    public void RemediationTasks_OnlyAllowListedCoordinators_MayReferenceTheProcessLauncherPort()
    {
        var assemblyPath = typeof(Grimoire.Hub.HubMetrics).Assembly.Location;
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        var violations = new List<string>();

        foreach (var module in assembly.Modules)
        {
            foreach (var (type, topLevel, effectiveNamespace) in module.Types.SelectMany(t => WithTopLevel(t, t, t.Namespace)))
            {
                if (!effectiveNamespace.StartsWith(RemediationTasksNamespace, StringComparison.Ordinal))
                    continue;

                // Nested types (async state machines, closures) count as their top-level
                // type: the coordinators' own awaited dispatch code stays allow-listed.
                if (_allowedCallerFullNames.Contains(topLevel.FullName))
                    continue;

                foreach (var reference in LauncherReferences(type))
                {
                    violations.Add($"{type.FullName} [{effectiveNamespace}] → {reference}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-018 (SC-005/FR-008, extended T042): within Grimoire.Hub.RemediationTasks, " +
            "only RemediationRunCoordinator and RemediationMessageTurnCoordinator may " +
            "reference the IAgentProcessLauncher port. Violations:\n" +
            string.Join("\n", violations));
    }

    /// <summary>
    /// Every place the type references <see cref="LauncherPortFullName"/>: fields,
    /// properties, method signatures, local variables, and method-body operands.
    /// </summary>
    private static IEnumerable<string> LauncherReferences(TypeDefinition type)
    {
        static bool Matches(TypeReference? reference)
            => reference is not null && reference.GetElementType().FullName == LauncherPortFullName;

        foreach (var field in type.Fields)
        {
            if (Matches(field.FieldType))
                yield return $"field {field.Name}";
        }

        foreach (var property in type.Properties)
        {
            if (Matches(property.PropertyType))
                yield return $"property {property.Name}";
        }

        foreach (var method in type.Methods)
        {
            if (Matches(method.ReturnType))
                yield return $"{method.Name} return type";

            foreach (var parameter in method.Parameters)
            {
                if (Matches(parameter.ParameterType))
                    yield return $"{method.Name} parameter {parameter.Name}";
            }

            if (!method.HasBody)
                continue;

            foreach (var variable in method.Body.Variables)
            {
                if (Matches(variable.VariableType))
                    yield return $"{method.Name} local variable";
            }

            foreach (var instruction in method.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case MethodReference callee when Matches(callee.DeclaringType):
                        yield return $"{method.Name} → {callee.DeclaringType.FullName}::{callee.Name}";
                        break;
                    case FieldReference fieldRef when Matches(fieldRef.FieldType) || Matches(fieldRef.DeclaringType):
                        yield return $"{method.Name} → field {fieldRef.FullName}";
                        break;
                    case TypeReference typeRef when Matches(typeRef):
                        yield return $"{method.Name} → typeref {typeRef.FullName}";
                        break;
                }
            }
        }
    }

    private static IEnumerable<(TypeDefinition Type, TypeDefinition TopLevel, string EffectiveNamespace)> WithTopLevel(
        TypeDefinition type, TypeDefinition topLevel, string parentNamespace)
    {
        var ns = string.IsNullOrEmpty(type.Namespace) ? parentNamespace : type.Namespace;
        yield return (type, topLevel, ns);
        foreach (var nested in type.NestedTypes.SelectMany(n => WithTopLevel(n, topLevel, ns)))
            yield return nested;
    }
}
