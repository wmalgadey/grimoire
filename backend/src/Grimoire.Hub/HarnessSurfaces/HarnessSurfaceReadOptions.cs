namespace Grimoire.Hub.HarnessSurfaces;

/// <summary>
/// T052 (022-align-wiki-structure, US3, ADR-023): the operator-controlled read scope over
/// the four reserved harness surfaces, bound from the top-level
/// <c>Grimoire:HarnessSurfaceReads</c> configuration key — same options-binding
/// convention as <see cref="Grimoire.Hub.LintDispatch.LintReviewWindowOptions"/> and
/// <see cref="Grimoire.Hub.QueryDispatch.QueryConcurrencyOptions"/>. This is harness
/// config plumbing only: the *decision* an operator makes here is theirs (FR-014), never
/// hard-coded — each property defaults to <c>false</c> (deny-by-default, FR-015), and all
/// four keys are written explicitly into <c>appsettings.json</c> as <c>false</c> so the
/// effective posture is visible rather than implied by absence. Applies uniformly to every
/// agent — there is no per-agent variant (spec clarification, 2026-08-09).
///
/// Deliberately NOT under <c>Grimoire:Paths</c>: that section is the single composition
/// point for runtime *locations* (<c>GrimoirePathResolver</c>/<c>ResolvedGrimoirePaths</c>)
/// and a grant set is not a location (ADR-023 "Decision Outcome").
/// </summary>
public sealed class HarnessSurfaceReadOptions
{
    public const string SectionName = "Grimoire:HarnessSurfaceReads";

    public bool Tasks { get; set; }

    public bool Conversations { get; set; }

    public bool Findings { get; set; }

    public bool RemediationTasks { get; set; }
}
