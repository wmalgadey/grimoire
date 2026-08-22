using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.LintAgent;

/// <summary>
/// Agent-process-side structured log events (plan.md ## Observability > Structured Log
/// Events) for the events that only make sense at the point instruction loading actually
/// happens — inside this process, not the Hub — plus the tool-denied/write-conflict/
/// write-lock-timeout events shared with Ingest/Query via the same
/// <c>GuardedToolExecutor</c> instrumentation seam. Mirrors
/// <c>Grimoire.QueryAgent.QueryAgentLogEvents</c>.
/// </summary>
public static class LintAgentLogEvents
{
    private static readonly EventId InstructionsLoadedEvent = new(70, "lint.instructions.loaded");
    private static readonly EventId InstructionsLoadFailedEvent = new(71, "lint.instructions.load_failed");
    private static readonly EventId ToolDeniedEvent = new(72, "lint.tool.denied");
    private static readonly EventId WriteConflictRejectedEvent = new(73, "wiki.write_conflict.rejected");
    private static readonly EventId WriteLockTimeoutEvent = new(74, "wiki.write_lock.timeout");

    // ── 026-guarded-tool-surface (ADR-030/ADR-031) ──────────────────────────────────────
    private static readonly EventId SearchTruncatedEvent = new(75, "wiki.search.truncated");
    private static readonly EventId SearchTimedOutEvent = new(76, "wiki.search.timed_out");
    private static readonly EventId SearchPatternRejectedEvent = new(77, "wiki.search.pattern_rejected");
    private static readonly EventId BatchRejectedEvent = new(78, "wiki.batch.rejected");
    private static readonly EventId PageDeletedEvent = new(79, "wiki.page.deleted");
    private static readonly EventId PageDeleteRolledBackEvent = new(80, "wiki.page.delete_rolled_back");

    public static void LogInstructionsLoaded(
        ILogger logger, string runId, string systemPromptSha256, int policyVersion, string policySha256)
    {
        using var span = StartLogEventSpan("lint.instructions.loaded", "Information");
        span?.SetTag("run_id", runId);
        span?.SetTag("system_prompt_sha256", systemPromptSha256);
        span?.SetTag("policy_version", policyVersion);
        span?.SetTag("policy_sha256", policySha256);

        logger.LogInformation(InstructionsLoadedEvent,
            "Lint instructions loaded. run_id={run_id} system_prompt_sha256={system_prompt_sha256} policy_version={policy_version} policy_sha256={policy_sha256}",
            runId, systemPromptSha256, policyVersion, policySha256);
    }

    public static void LogInstructionsLoadFailed(ILogger logger, string runId, string reason)
    {
        using var span = StartLogEventSpan("lint.instructions.load_failed", "Error");
        span?.SetTag("run_id", runId);
        span?.SetTag("reason", reason);

        logger.LogError(InstructionsLoadFailedEvent,
            "Lint instructions failed to load. run_id={run_id} reason={reason}",
            runId, reason);
    }

    public static void LogToolDenied(ILogger logger, string runId, string tool, string target, string reason, int turn)
    {
        using var span = StartLogEventSpan("lint.tool.denied", "Warning");
        span?.SetTag("run_id", runId);
        span?.SetTag("tool", tool);
        span?.SetTag("target", target);
        span?.SetTag("reason", reason);
        span?.SetTag("turn", turn);

        logger.LogWarning(ToolDeniedEvent,
            "Lint tool call denied. run_id={run_id} tool={tool} target={target} reason={reason} turn={turn}",
            runId, tool, target, reason, turn);
    }

    /// <summary>Shared signal (ADR-015, plan.md ## Observability note): field name is
    /// <c>task_id</c> — the emission point (<c>GuardedToolExecutor</c>) is shared harness
    /// code, not Lint-specific; <paramref name="runId"/> is Lint's run id passed through
    /// as the executor's <c>taskId</c>.</summary>
    public static void LogWriteConflictRejected(ILogger logger, string runId, string path, string reason, int turn)
    {
        using var span = StartLogEventSpan("wiki.write_conflict.rejected", "Warning");
        span?.SetTag("task_id", runId);
        span?.SetTag("path", path);
        span?.SetTag("reason", reason);
        span?.SetTag("turn", turn);

        logger.LogWarning(WriteConflictRejectedEvent,
            "Write rejected by coordination guard. task_id={task_id} path={path} reason={reason} turn={turn}",
            runId, path, reason, turn);
    }

    public static void LogWriteLockTimeout(ILogger logger, string runId, string path, double waitMs)
    {
        using var span = StartLogEventSpan("wiki.write_lock.timeout", "Warning");
        span?.SetTag("task_id", runId);
        span?.SetTag("path", path);
        span?.SetTag("wait_ms", waitMs);

        logger.LogWarning(WriteLockTimeoutEvent,
            "Write-coordination lock acquisition timed out. task_id={task_id} path={path} wait_ms={wait_ms}",
            runId, path, waitMs);
    }

    // ── 026-guarded-tool-surface (ADR-030/ADR-031) ──────────────────────────────────────

    public static void LogSearchTruncated(ILogger logger, string runId, int patternLength, int cap, int turn)
    {
        using var span = StartLogEventSpan("wiki.search.truncated", "Warning");
        span?.SetTag("task_id", runId);
        span?.SetTag("run_id", runId);
        span?.SetTag("pattern_length", patternLength);
        span?.SetTag("cap", cap);
        span?.SetTag("turn", turn);

        logger.LogWarning(SearchTruncatedEvent,
            "Search result cap reached. task_id={task_id} run_id={run_id} pattern_length={pattern_length} cap={cap} turn={turn}",
            runId, runId, patternLength, cap, turn);
    }

    public static void LogSearchTimedOut(ILogger logger, string runId, double budgetMs, int filesScanned, int turn)
    {
        using var span = StartLogEventSpan("wiki.search.timed_out", "Warning");
        span?.SetTag("task_id", runId);
        span?.SetTag("run_id", runId);
        span?.SetTag("budget_ms", budgetMs);
        span?.SetTag("files_scanned", filesScanned);
        span?.SetTag("turn", turn);

        logger.LogWarning(SearchTimedOutEvent,
            "Search time budget exhausted mid-scan. task_id={task_id} run_id={run_id} budget_ms={budget_ms} files_scanned={files_scanned} turn={turn}",
            runId, runId, budgetMs, filesScanned, turn);
    }

    public static void LogSearchPatternRejected(ILogger logger, string runId, string reason, int patternLength, int turn)
    {
        using var span = StartLogEventSpan("wiki.search.pattern_rejected", "Warning");
        span?.SetTag("task_id", runId);
        span?.SetTag("run_id", runId);
        span?.SetTag("reason", reason);
        span?.SetTag("pattern_length", patternLength);
        span?.SetTag("turn", turn);

        logger.LogWarning(SearchPatternRejectedEvent,
            "Search pattern rejected. task_id={task_id} run_id={run_id} reason={reason} pattern_length={pattern_length} turn={turn}",
            runId, runId, reason, patternLength, turn);
    }

    public static void LogBatchRejected(ILogger logger, string runId, string reason, int callCount, int turn)
    {
        using var span = StartLogEventSpan("wiki.batch.rejected", "Warning");
        span?.SetTag("task_id", runId);
        span?.SetTag("run_id", runId);
        span?.SetTag("reason", reason);
        span?.SetTag("call_count", callCount);
        span?.SetTag("turn", turn);

        logger.LogWarning(BatchRejectedEvent,
            "Batch call rejected. task_id={task_id} run_id={run_id} reason={reason} call_count={call_count} turn={turn}",
            runId, runId, reason, callCount, turn);
    }

    public static void LogPageDeleted(ILogger logger, string runId, string path, int turn)
    {
        using var span = StartLogEventSpan("wiki.page.deleted", "Information");
        span?.SetTag("task_id", runId);
        span?.SetTag("run_id", runId);
        span?.SetTag("path", path);
        span?.SetTag("turn", turn);

        logger.LogInformation(PageDeletedEvent,
            "Page deleted through the guarded boundary. task_id={task_id} run_id={run_id} path={path} turn={turn}",
            runId, runId, path, turn);
    }

    public static void LogPageDeleteRolledBack(ILogger logger, string runId, string path, int turn)
    {
        using var span = StartLogEventSpan("wiki.page.delete_rolled_back", "Warning");
        span?.SetTag("task_id", runId);
        span?.SetTag("run_id", runId);
        span?.SetTag("path", path);
        span?.SetTag("turn", turn);

        logger.LogWarning(PageDeleteRolledBackEvent,
            "Journaled deletion restored during rollback. task_id={task_id} run_id={run_id} path={path} turn={turn}",
            runId, runId, path, turn);
    }

    private static Activity? StartLogEventSpan(string eventName, string level)
    {
        var span = LintAgentTracing.ActivitySource.StartActivity(eventName);
        span?.SetTag("signal_type", "log");
        span?.SetTag("event_name", eventName);
        span?.SetTag("level", level);
        return span;
    }
}
