using System.Diagnostics;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Microsoft.Extensions.Logging;

namespace Grimoire.LintAgent;

/// <summary>
/// Wires the shared <c>AgentLoop</c>'s instrumentation seam to Lint's own
/// <c>lint_agent.model_turn</c> span shape. Mirrors
/// <c>Grimoire.QueryAgent.QueryAgentLoopInstrumentation</c>.
/// </summary>
public sealed class LintAgentLoopInstrumentation : IAgentLoopInstrumentation
{
    public Activity? StartModelTurnActivity(string taskId, int turn)
    {
        var span = LintAgentTracing.ActivitySource.StartActivity("lint_agent.model_turn");
        span?.SetTag("run_id", taskId);
        span?.SetTag("turn", turn);
        return span;
    }

    // plan.md's Observability table declares no per-agent turn/token-cap/no-tool-turn
    // metrics for Lint — intentionally no-ops rather than emitting metrics the spec
    // doesn't declare (mirrors QueryAgentLoopInstrumentation).
    public void RecordAgentTurns(int turns, string outcome) { }
    public void RecordModelTokens(int inputTokens, int outputTokens) { }
    public void RecordModelToolRequests(int toolRequestCount, ModelStopReason stopReason) { }
    public void RecordNoToolTurn(ModelStopReason stopReason, string outcome) { }
}

/// <summary>
/// Wires the shared <c>GuardedToolExecutor</c>'s instrumentation seam to Lint's own
/// <c>lint_agent.tool_call</c> span shape (plan.md ## Observability), <c>lint.tool_calls_total</c>
/// metric, and <c>lint.tool.denied</c> log event. Mirrors
/// <c>Grimoire.QueryAgent.QueryToolCallInstrumentation</c>.
/// </summary>
public sealed class LintToolCallInstrumentation : IToolCallInstrumentation
{
    private readonly ILogger _logger;

    public LintToolCallInstrumentation(ILogger logger)
    {
        _logger = logger;
    }

    public void RecordAllowed(string taskId, string tool, string target, int turn)
    {
        using var span = LintAgentTracing.ActivitySource.StartActivity("lint_agent.tool_call");
        span?.SetTag("run_id", taskId);
        span?.SetTag("tool", tool);
        span?.SetTag("target", target);
        span?.SetTag("decision", "allowed");
        span?.SetTag("turn", turn);

        LintAgentMetrics.RecordToolCall(tool, "allowed");
    }

    public void RecordDenied(string taskId, string tool, string requestedTarget, string canonicalTarget, string reason, int turn)
    {
        using var span = LintAgentTracing.ActivitySource.StartActivity("lint_agent.tool_call");
        span?.SetTag("run_id", taskId);
        span?.SetTag("tool", tool);
        span?.SetTag("target", canonicalTarget);
        span?.SetTag("requested_target", requestedTarget);
        span?.SetTag("decision", "denied");
        span?.SetTag("turn", turn);

        LintAgentMetrics.RecordToolCall(tool, "denied");
        LintAgentLogEvents.LogToolDenied(_logger, taskId, tool, canonicalTarget, reason, turn);
    }

    /// <summary>ADR-016: Lint's write scope is frontmatter-only — there is no create-only
    /// rule and therefore no successful create-only write to report; never invoked in
    /// practice (no write rule in <c>data/agents/lint/policy.json</c> sets
    /// <c>WriteMode.CreateOnly</c>), but implemented for interface completeness.</summary>
    public void RecordCreateOnlyWriteSucceeded(string taskId, string path, int turn)
    {
    }

    public void RecordWriteConflictRejected(string taskId, string path, string reason, int turn)
    {
        LintAgentMetrics.RecordWriteConflictRejected(reason);
        LintAgentLogEvents.LogWriteConflictRejected(_logger, taskId, path, reason, turn);
    }

    public Activity? StartAcquireWriteLockActivity(string taskId, string path, int turn)
    {
        var span = LintAgentTracing.ActivitySource.StartActivity("guardrails.acquire_write_lock");
        span?.SetTag("run_id", taskId);
        span?.SetTag("turn", turn);
        return span;
    }

    public void RecordWriteLockAcquisition(string taskId, string path, string outcome, double waitSeconds, int turn)
    {
        LintAgentMetrics.RecordWriteLockAcquisition(outcome, waitSeconds);

        if (outcome == "timeout")
        {
            LintAgentLogEvents.LogWriteLockTimeout(_logger, taskId, path, waitSeconds * 1000);
        }
    }

    // ── 026-guarded-tool-surface (ADR-030/ADR-031): registered here (T014) ahead of the
    // story phases that call them (Phase 3 search, Phase 4 delete, Phase 5 read shape,
    // Phase 6 batch) — the production composition root for these signals is wired once,
    // so each story phase only adds the call site.

    public void RecordSearchInvocation(string taskId, string outcome, int matchesReturned, int filesScanned, int turn)
        => LintAgentMetrics.RecordSearchInvocation(outcome, matchesReturned, filesScanned);

    public void LogSearchTruncated(string taskId, int patternLength, int cap, int turn)
        => LintAgentLogEvents.LogSearchTruncated(_logger, taskId, patternLength, cap, turn);

    public void LogSearchTimedOut(string taskId, double budgetMs, int filesScanned, int turn)
        => LintAgentLogEvents.LogSearchTimedOut(_logger, taskId, budgetMs, filesScanned, turn);

    public void LogSearchPatternRejected(string taskId, string reason, int patternLength, int turn)
        => LintAgentLogEvents.LogSearchPatternRejected(_logger, taskId, reason, patternLength, turn);

    public Activity? StartSearchScanActivity(string taskId, int turn)
    {
        var span = LintAgentTracing.ActivitySource.StartActivity("guardrails.search_scan");
        span?.SetTag("task_id", taskId);
        span?.SetTag("turn", turn);
        return span;
    }

    public void RecordReadInvocation(string taskId, string shape, int turn)
        => LintAgentMetrics.RecordReadInvocation(shape);

    public void RecordBatchInvocation(string taskId, string outcome, int turn)
        => LintAgentMetrics.RecordBatchInvocation(outcome);

    public void LogBatchRejected(string taskId, string reason, int callCount, int turn)
        => LintAgentLogEvents.LogBatchRejected(_logger, taskId, reason, callCount, turn);

    public Activity? StartBatchActivity(string taskId, int turn)
    {
        var span = LintAgentTracing.ActivitySource.StartActivity("guardrails.batch");
        span?.SetTag("task_id", taskId);
        span?.SetTag("turn", turn);
        return span;
    }

    public void RecordDeletion(string taskId, string outcome, int turn)
        => LintAgentMetrics.RecordDeletion(outcome);

    public void LogPageDeleted(string taskId, string path, int turn)
        => LintAgentLogEvents.LogPageDeleted(_logger, taskId, path, turn);

    public void LogPageDeleteRolledBack(string taskId, string path, int turn)
        => LintAgentLogEvents.LogPageDeleteRolledBack(_logger, taskId, path, turn);

    public Activity? StartDeleteFileActivity(string taskId, string path, int turn)
    {
        var span = LintAgentTracing.ActivitySource.StartActivity("guardrails.delete_file");
        span?.SetTag("task_id", taskId);
        span?.SetTag("path", path);
        span?.SetTag("turn", turn);
        return span;
    }
}
