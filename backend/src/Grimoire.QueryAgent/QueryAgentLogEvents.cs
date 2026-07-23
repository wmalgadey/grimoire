using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.QueryAgent;

/// <summary>
/// Agent-process-side structured log events (plan.md ## Observability > Structured Log
/// Events, 008-query-agent) for the events that only make sense at the point instruction
/// loading actually happens — inside this process, not the Hub. Mirrors
/// <c>Grimoire.IngestAgent.IngestAgentLogEvents</c>'s instructions-loaded/load-failed
/// pair and its tool-denied event (emitted from the shared
/// <c>GuardedToolExecutor</c>/<c>AgentLoop</c> instrumentation seam via
/// <see cref="QueryAgentInstrumentation"/>).
/// </summary>
public static class QueryAgentLogEvents
{
    private static readonly EventId InstructionsLoadedEvent = new(60, "query.instructions.loaded");
    private static readonly EventId InstructionsLoadFailedEvent = new(61, "query.instructions.load_failed");
    private static readonly EventId ToolDeniedEvent = new(62, "query.tool.denied");

    public static void LogInstructionsLoaded(
        ILogger logger, string turnId, string systemPromptSha256, int policyVersion, string policySha256)
    {
        using var span = StartLogEventSpan("query.instructions.loaded", "Information");
        span?.SetTag("turn_id", turnId);
        span?.SetTag("system_prompt_sha256", systemPromptSha256);
        span?.SetTag("policy_version", policyVersion);
        span?.SetTag("policy_sha256", policySha256);

        logger.LogInformation(InstructionsLoadedEvent,
            "Query instructions loaded. turn_id={turn_id} system_prompt_sha256={system_prompt_sha256} policy_version={policy_version} policy_sha256={policy_sha256}",
            turnId, systemPromptSha256, policyVersion, policySha256);
    }

    public static void LogInstructionsLoadFailed(ILogger logger, string turnId, string reason)
    {
        using var span = StartLogEventSpan("query.instructions.load_failed", "Error");
        span?.SetTag("turn_id", turnId);
        span?.SetTag("reason", reason);

        logger.LogError(InstructionsLoadFailedEvent,
            "Query instructions failed to load. turn_id={turn_id} reason={reason}",
            turnId, reason);
    }

    public static void LogToolDenied(ILogger logger, string turnId, string tool, string target, string reason, int turn)
    {
        using var span = StartLogEventSpan("query.tool.denied", "Warning");
        span?.SetTag("turn_id", turnId);
        span?.SetTag("tool", tool);
        span?.SetTag("target", target);
        span?.SetTag("reason", reason);
        span?.SetTag("turn", turn);

        logger.LogWarning(ToolDeniedEvent,
            "Query tool call denied. turn_id={turn_id} tool={tool} target={target} reason={reason} turn={turn}",
            turnId, tool, target, reason, turn);
    }

    private static Activity? StartLogEventSpan(string eventName, string level)
    {
        var span = QueryAgentTracing.ActivitySource.StartActivity(eventName);
        span?.SetTag("signal_type", "log");
        span?.SetTag("event_name", eventName);
        span?.SetTag("level", level);
        return span;
    }
}
