using Grimoire.Domain.Guardrails;

namespace Grimoire.Hub.HarnessSurfaces;

/// <summary>
/// T051/T059 (022-align-wiki-structure, US3, ADR-023): maps
/// <see cref="HarnessSurfaceReadOptions"/>' four booleans to the ordered granted-surface
/// list threaded to every agent spawn as the <c>--granted-harness-surfaces</c> CLI
/// argument (contracts/harness-surface-read-scope.md "Delivery to the agent"). References
/// <see cref="ReservedHarnessSurfaces"/>' named members rather than re-declaring the
/// literals (ADR-023 H2) — this file's own text never contains all four reserved-surface
/// string literals together.
/// </summary>
public static class HarnessSurfaceGrantResolver
{
    /// <summary>The granted subset, in <see cref="ReservedHarnessSurfaces.All"/>'s fixed order.</summary>
    public static IReadOnlyList<string> ResolveGranted(HarnessSurfaceReadOptions options)
    {
        var granted = new List<string>();

        if (options.Tasks)
        {
            granted.Add(ReservedHarnessSurfaces.Tasks);
        }

        if (options.Conversations)
        {
            granted.Add(ReservedHarnessSurfaces.Conversations);
        }

        if (options.Findings)
        {
            granted.Add(ReservedHarnessSurfaces.Findings);
        }

        if (options.RemediationTasks)
        {
            granted.Add(ReservedHarnessSurfaces.RemediationTasks);
        }

        return granted;
    }
}
