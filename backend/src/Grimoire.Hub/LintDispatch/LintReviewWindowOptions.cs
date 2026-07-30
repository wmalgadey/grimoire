namespace Grimoire.Hub.LintDispatch;

/// <summary>
/// T036 (013-lint-agent, US2): the configurable age threshold (spec.md "Review Window")
/// after which a low-confidence page is due for a fresh look, bound from the top-level
/// <c>Grimoire:LintReviewWindowDays</c> configuration key — same options-binding
/// convention as <see cref="Grimoire.Hub.QueryDispatch.QueryConcurrencyOptions"/>. This is
/// harness config plumbing only: the *decision* that a given page qualifies as a review
/// candidate remains the agent's judgment (Constitution Principle V,
/// <c>data/agents/lint/system-prompt.md</c>'s "Review candidates" rule) — the Hub's only
/// job is threading the effective window value into the run's context
/// (<see cref="LintAgentRequest.ReviewWindowDays"/>) so the agent's default (also 90,
/// stated in the system prompt) can be overridden without an instruction-file edit.
/// </summary>
public sealed class LintReviewWindowOptions
{
    public const string SectionName = "Grimoire";

    public int LintReviewWindowDays { get; set; } = 90;
}
