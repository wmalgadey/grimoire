using Grimoire.Domain.Guardrails;

namespace Grimoire.AgentRuntime.HarnessSurfaces;

/// <summary>
/// ADR-023 (022-align-wiki-structure, Phase 5): maps the granted-surface CLI argument
/// (the ordered list of reserved-surface names an operator's
/// <c>Grimoire.Hub.HarnessSurfaces.HarnessSurfaceReadOptions</c> grants) to its complement
/// within <see cref="ReservedHarnessSurfaces.All"/> — the set of canonical, directory-style
/// (trailing separator) absolute paths <c>Grimoire.Domain.Guardrails.SafetyPolicy</c>'s
/// denied-read-subtree narrowing needs. Lives once here (in
/// <see cref="Grimoire.AgentRuntime.Host.AgentHost"/>'s shared startup template, the one
/// place every one of the five spawn modes — Ingest, Query, Lint's three modes — passes
/// through) rather than being duplicated across each agent's own composition root, so the
/// mapping is applied uniformly with nothing to keep in sync (Constitution Principle I:
/// the boolean→subtree mapping stays out of the dependency-free Domain).
/// </summary>
public static class HarnessSurfaceReadScope
{
    /// <summary>
    /// Resolves the denied-read-subtree list for one run: every reserved surface name NOT
    /// present in <paramref name="grantedSurfaces"/>, each mapped to a canonical
    /// directory-style absolute path under <paramref name="wikiRoot"/> — the same
    /// normalization <c>PolicyLoader.NormalizeRulePrefix</c> applies to a directory-style
    /// policy-file prefix, ensuring <c>SafetyPolicy.PrefixMatches</c>' trailing-separator
    /// branch matches the subtree AND the bare directory itself.
    /// </summary>
    public static IReadOnlyList<string> ResolveDeniedSubtreePaths(
        string wikiRoot, IReadOnlyList<string>? grantedSurfaces)
    {
        var grantedSet = new HashSet<string>(grantedSurfaces ?? [], StringComparer.Ordinal);

        var denied = new List<string>();
        foreach (var surface in ReservedHarnessSurfaces.All)
        {
            if (grantedSet.Contains(surface))
            {
                continue;
            }

            var canonical = Path.GetFullPath(Path.Combine(wikiRoot, surface));
            denied.Add(canonical.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        }

        return denied;
    }
}
