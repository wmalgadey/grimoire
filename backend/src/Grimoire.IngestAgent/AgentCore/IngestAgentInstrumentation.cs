using System.Diagnostics;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Microsoft.Extensions.Logging;

namespace Grimoire.IngestAgent.AgentCore;

/// <summary>
/// Wires the shared <c>AgentLoop</c>'s instrumentation seam (ADR-011) to Ingest's
/// existing <see cref="IngestAgentTracing"/>/<see cref="IngestAgentMetrics"/> statics —
/// preserves the exact <c>ingest_agent.model_turn</c> span shape and
/// <c>wiki.ingest.*</c> metrics emitted before the Grimoire.AgentRuntime extraction.
/// </summary>
public sealed class IngestAgentLoopInstrumentation : IAgentLoopInstrumentation
{
    public Activity? StartModelTurnActivity(string taskId, int turn)
    {
        var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.model_turn");
        span?.SetTag("task_id", taskId);
        span?.SetTag("turn", turn);
        return span;
    }

    public void RecordAgentTurns(int turns, string outcome)
        => IngestAgentMetrics.RecordAgentTurns(turns, outcome);

    public void RecordModelTokens(int inputTokens, int outputTokens)
        => IngestAgentMetrics.RecordModelTokens(inputTokens, outputTokens);

    public void RecordModelToolRequests(int toolRequestCount, ModelStopReason stopReason)
        => IngestAgentMetrics.RecordModelToolRequests(toolRequestCount, stopReason);

    public void RecordNoToolTurn(ModelStopReason stopReason, string outcome)
        => IngestAgentMetrics.RecordNoToolTurn(stopReason, outcome);
}

/// <summary>
/// Wires the shared <c>GuardedToolExecutor</c>'s instrumentation seam (ADR-011) to
/// Ingest's existing <c>ingest_agent.tool_call</c> span shape,
/// <c>wiki.ingest.tool_calls_total</c>/<c>actions_denied_total</c> metrics, and
/// <c>ingest.tool.allowed</c>/<c>ingest.tool.denied</c> log events.
/// </summary>
public sealed class IngestToolCallInstrumentation : IToolCallInstrumentation
{
    private readonly ILogger _logger;

    public IngestToolCallInstrumentation(ILogger logger)
    {
        _logger = logger;
    }

    public void RecordAllowed(string taskId, string tool, string target, int turn)
    {
        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.tool_call");
        span?.SetTag("task_id", taskId);
        span?.SetTag("tool", tool);
        span?.SetTag("target", target);
        span?.SetTag("decision", "allowed");
        span?.SetTag("turn", turn);

        IngestAgentMetrics.RecordToolCall(tool, "allowed");
        IngestAgentLogEvents.LogToolAllowed(_logger, taskId, tool, target, turn);
    }

    public void RecordDenied(string taskId, string tool, string requestedTarget, string canonicalTarget, string reason, int turn)
    {
        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.tool_call");
        span?.SetTag("task_id", taskId);
        span?.SetTag("tool", tool);
        span?.SetTag("target", canonicalTarget);
        span?.SetTag("requested_target", requestedTarget);
        span?.SetTag("decision", "denied");
        span?.SetTag("turn", turn);

        IngestAgentMetrics.RecordToolCall(tool, "denied");
        IngestAgentMetrics.RecordActionDenied(tool, reason);
        IngestAgentLogEvents.LogToolDenied(_logger, taskId, tool, canonicalTarget, reason, turn);
    }
}
