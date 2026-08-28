using System.Diagnostics;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.AgentRuntime.Telemetry;

namespace Grimoire.LintAgent;

/// <summary>
/// Lint's frozen tracing identities (plan.md ## Observability: source
/// <c>Grimoire.LintAgent</c>, root span <c>lint_agent.run</c>, correlation attribute
/// <c>run_id</c>), delegating to the shared <see cref="AgentTracing"/> scaffold
/// (ADR-013) — the TRACEPARENT/TRACESTATE parenting that links the Hub's
/// <c>hub.lint.trigger</c>/spawn span to this agent run into a single end-to-end trace
/// lives once in the platform.
/// </summary>
public static class LintAgentTracing
{
    private static readonly AgentTracing _tracing = new(
        "Grimoire.LintAgent", "lint_agent.run", "run_id");

    public static ActivitySource ActivitySource => _tracing.ActivitySource;

    public static Activity? StartRunActivity(string runId)
        => _tracing.StartRunActivity(runId);

    /// <summary>
    /// 028-lint-at-scale (US2, FR-003): tags the current <c>lint_agent.run</c> root span
    /// (<see cref="Activity.Current"/> — <c>RunLintRunAsync</c>'s <c>runSpan</c> is still
    /// the ambient activity at the point <c>LintIntentHandler</c> computes this, since
    /// every nested tool-call span it started along the way has already stopped and
    /// restored it) with the harness-computed coverage report. A no-op when tracing is
    /// disabled (no current activity) — mirrors every other <c>?.SetTag</c> call site.
    /// </summary>
    public static void RecordCoverageOnCurrentRun(WikiCoverage coverage)
    {
        var activity = Activity.Current;
        activity?.SetTag("coverage.pages_total", coverage.PagesTotal);
        activity?.SetTag("coverage.pages_considered", coverage.PagesConsidered);
        activity?.SetTag("coverage.status", coverage.Status);
    }
}
