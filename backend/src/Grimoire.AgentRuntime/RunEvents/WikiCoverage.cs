namespace Grimoire.AgentRuntime.RunEvents;

/// <summary>
/// 028-lint-at-scale (US2, FR-003/FR-004): harness-computed report of how much of the wiki
/// a Lint run actually looked at — computed once, at run completion, from
/// <see cref="Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor.ConsideredPaths"/> plus a
/// page-count snapshot taken at run start (data-model.md "WikiCoverage", contracts/
/// coverage-signal.md). Never self-reported by the agent's own narrative — the agent's
/// final message plays no role in producing this value (Constitution Principle V).
///
/// Orthogonal to <c>LintFindingsReport.Partial</c> (which means "this run crashed mid-analysis"):
/// a run can finish cleanly (<c>Partial == false</c>) while its own <see cref="Status"/> is
/// <see cref="StatusPartial"/> — it succeeded, but by design or budget did not touch every
/// page (data-model.md).
/// </summary>
public sealed record WikiCoverage(int PagesTotal, int PagesConsidered, string Status)
{
    public const string StatusComplete = "complete";
    public const string StatusPartial = "partial";

    public static WikiCoverage Compute(int pagesTotal, int pagesConsidered)
        => new(pagesTotal, pagesConsidered, pagesConsidered == pagesTotal ? StatusComplete : StatusPartial);
}
