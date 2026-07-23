using System.Diagnostics;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Microsoft.Extensions.Logging;

namespace Grimoire.QueryAgent;

/// <summary>
/// Wires the shared <c>AgentLoop</c>'s instrumentation seam (ADR-011) to Query's own
/// <c>query_agent.model_turn</c> span shape (plan.md Observability). Mirrors
/// <c>Grimoire.IngestAgent.AgentCore.IngestAgentLoopInstrumentation</c>.
/// </summary>
public sealed class QueryAgentLoopInstrumentation : IAgentLoopInstrumentation
{
    public Activity? StartModelTurnActivity(string taskId, int turn)
    {
        var span = QueryAgentTracing.ActivitySource.StartActivity("query_agent.model_turn");
        span?.SetTag("turn_id", taskId);
        span?.SetTag("turn", turn);
        return span;
    }

    // Query has no per-agent turn/token-cap/no-tool-turn metrics of its own in
    // plan.md's Observability table (unlike Ingest's wiki.ingest.* set) — these are
    // intentionally no-ops rather than emitting metrics the spec doesn't declare.
    public void RecordAgentTurns(int turns, string outcome) { }
    public void RecordModelTokens(int inputTokens, int outputTokens) { }
    public void RecordModelToolRequests(int toolRequestCount, ModelStopReason stopReason) { }
    public void RecordNoToolTurn(ModelStopReason stopReason, string outcome) { }
}

/// <summary>
/// Wires the shared <c>GuardedToolExecutor</c>'s instrumentation seam (ADR-011) to
/// Query's own <c>query_agent.tool_call</c> span shape, <c>query.tool_calls_total</c>
/// metric, and <c>query.tool.denied</c> log event. Mirrors
/// <c>Grimoire.IngestAgent.AgentCore.IngestToolCallInstrumentation</c>.
/// </summary>
public sealed class QueryToolCallInstrumentation : IToolCallInstrumentation
{
    private readonly ILogger _logger;

    public QueryToolCallInstrumentation(ILogger logger)
    {
        _logger = logger;
    }

    public void RecordAllowed(string taskId, string tool, string target, int turn)
    {
        using var span = QueryAgentTracing.ActivitySource.StartActivity("query_agent.tool_call");
        span?.SetTag("turn_id", taskId);
        span?.SetTag("tool", tool);
        span?.SetTag("target", target);
        span?.SetTag("decision", "allowed");
        span?.SetTag("turn", turn);

        QueryAgentMetrics.RecordToolCall(tool, "allowed");
    }

    public void RecordDenied(string taskId, string tool, string requestedTarget, string canonicalTarget, string reason, int turn)
    {
        using var span = QueryAgentTracing.ActivitySource.StartActivity("query_agent.tool_call");
        span?.SetTag("turn_id", taskId);
        span?.SetTag("tool", tool);
        span?.SetTag("target", canonicalTarget);
        span?.SetTag("requested_target", requestedTarget);
        span?.SetTag("decision", "denied");
        span?.SetTag("turn", turn);

        QueryAgentMetrics.RecordToolCall(tool, "denied");
        QueryAgentLogEvents.LogToolDenied(_logger, taskId, tool, canonicalTarget, reason, turn);
    }
}
