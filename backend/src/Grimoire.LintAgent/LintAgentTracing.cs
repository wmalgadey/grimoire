using System.Diagnostics;
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
}
