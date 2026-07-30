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
    private static readonly EventId SynthesisPageCreatedEvent = new(63, "wiki.query.synthesis_page_created");
    private static readonly EventId WriteConflictRejectedEvent = new(64, "wiki.write_conflict.rejected");
    private static readonly EventId WriteLockTimeoutEvent = new(65, "wiki.write_lock.timeout");

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

    /// <summary>
    /// ADR-015 (012-query-synthesis-writes), plan.md ## Observability > Structured Log
    /// Events: a create-only write succeeded — a new Synthesis Page was created. Field
    /// name is <c>task_id</c> (not this file's usual <c>turn_id</c>) — plan.md declares
    /// this event's mandatory fields as <c>task_id</c>/<c>path</c>/<c>turn</c> because the
    /// emission point (<c>GuardedToolExecutor</c>) is shared harness code, not
    /// Query-specific; <paramref name="taskId"/> is Query's turn id passed through as the
    /// executor's <c>taskId</c>.
    /// </summary>
    public static void LogSynthesisPageCreated(ILogger logger, string taskId, string path, int turn)
    {
        using var span = StartLogEventSpan("wiki.query.synthesis_page_created", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("path", path);
        span?.SetTag("turn", turn);

        logger.LogInformation(SynthesisPageCreatedEvent,
            "Synthesis page created. task_id={task_id} path={path} turn={turn}",
            taskId, path, turn);
    }

    /// <summary>
    /// T034 (012-query-synthesis-writes, US2), plan.md ## Observability > Structured Log
    /// Events: a write was rejected by the write-coordination guard's create-only or
    /// compare-and-swap check (<paramref name="reason"/> is <c>create_only_target_exists</c>
    /// or <c>write_conflict_stale_read</c>). Field name is <c>task_id</c>, matching
    /// <see cref="LogSynthesisPageCreated"/> for the same reason — the emission point
    /// (<c>GuardedToolExecutor</c>) is shared harness code, not Query-specific.
    /// </summary>
    public static void LogWriteConflictRejected(ILogger logger, string taskId, string path, string reason, int turn)
    {
        using var span = StartLogEventSpan("wiki.write_conflict.rejected", "Warning");
        span?.SetTag("task_id", taskId);
        span?.SetTag("path", path);
        span?.SetTag("reason", reason);
        span?.SetTag("turn", turn);

        logger.LogWarning(WriteConflictRejectedEvent,
            "Write rejected by coordination guard. task_id={task_id} path={path} reason={reason} turn={turn}",
            taskId, path, reason, turn);
    }

    /// <summary>
    /// T042 (012-query-synthesis-writes, US3), plan.md ## Observability > Structured Log
    /// Events: lock acquisition exceeded the bounded backoff cap
    /// (<c>write_coordination_timeout</c>). Field name is <c>task_id</c>, matching
    /// <see cref="LogSynthesisPageCreated"/> for the same reason — the emission point
    /// (<c>GuardedToolExecutor</c>) is shared harness code, not Query-specific.
    /// </summary>
    public static void LogWriteLockTimeout(ILogger logger, string taskId, string path, double waitMs)
    {
        using var span = StartLogEventSpan("wiki.write_lock.timeout", "Warning");
        span?.SetTag("task_id", taskId);
        span?.SetTag("path", path);
        span?.SetTag("wait_ms", waitMs);

        logger.LogWarning(WriteLockTimeoutEvent,
            "Write-coordination lock acquisition timed out. task_id={task_id} path={path} wait_ms={wait_ms}",
            taskId, path, waitMs);
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
