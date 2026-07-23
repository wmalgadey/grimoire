using Grimoire.AgentRuntime.Guardrails;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Grimoire.IngestAgent;

public static class IngestAgentLogEvents
{
    private static readonly EventId InstructionsLoadedEvent = new(1, "ingest.instructions.loaded");
    private static readonly EventId InstructionsLoadFailedEvent = new(2, "ingest.instructions.load_failed");
    private static readonly EventId ToolAllowedEvent = new(3, "ingest.tool.allowed");
    private static readonly EventId ToolDeniedEvent = new(4, "ingest.tool.denied");
    private static readonly EventId RunRolledBackEvent = new(5, "ingest.run.rolled_back");
    private static readonly EventId BackstopAppendedEvent = new(6, "ingest.log.backstop_appended");
    private static readonly EventId AgentCompletedEvent = new(7, "ingest.agent.completed");
    private static readonly EventId AgentCapExceededEvent = new(8, "ingest.agent.cap_exceeded");
    private static readonly EventId UserPromptResolvedEvent = new(9, "ingest.agent.user_prompt_resolved");

    public static void LogInstructionsLoaded(
        ILogger logger,
        string taskId,
        string path,
        string sha256,
        int policyVersion,
        string policySha256)
    {
        using var span = StartLogEventSpan(InstructionsLoadedEvent.Name ?? "ingest.instructions.loaded", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("path", path);
        span?.SetTag("sha256", sha256);
        span?.SetTag("policy_version", policyVersion);
        span?.SetTag("policy_sha256", policySha256);

        logger.LogInformation(
            InstructionsLoadedEvent,
            "System prompt and policy loaded for task {task_id}. path={path} sha256={sha256} policy_version={policy_version} policy_sha256={policy_sha256}",
            taskId,
            path,
            sha256,
            policyVersion,
            policySha256);
    }

    public static void LogUserPromptResolved(
        ILogger logger,
        string taskId,
        string promptSource,
        int promptLength)
    {
        using var span = StartLogEventSpan(UserPromptResolvedEvent.Name ?? "ingest.agent.user_prompt_resolved", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("prompt_source", promptSource);
        span?.SetTag("prompt_length", promptLength);

        logger.LogInformation(
            UserPromptResolvedEvent,
            "Effective user prompt resolved for task {task_id}. prompt_source={prompt_source} prompt_length={prompt_length}",
            taskId,
            promptSource,
            promptLength);
    }

    public static void LogInstructionsLoadFailed(
        ILogger logger,
        string taskId,
        string artifact,
        string path,
        string reason)
    {
        using var span = StartLogEventSpan(InstructionsLoadFailedEvent.Name ?? "ingest.instructions.load_failed", "Error");
        span?.SetTag("task_id", taskId);
        span?.SetTag("artifact", artifact);
        span?.SetTag("path", path);
        span?.SetTag("reason", reason);

        logger.LogError(
            InstructionsLoadFailedEvent,
            "Instruction/policy load failed for task {task_id}. artifact={artifact} path={path} reason={reason}",
            taskId,
            artifact,
            path,
            reason);
    }

    public static void LogToolAllowed(ILogger logger, string taskId, string tool, string target, int turn)
    {
        using var span = StartLogEventSpan(ToolAllowedEvent.Name ?? "ingest.tool.allowed", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("tool", tool);
        span?.SetTag("target", target);
        span?.SetTag("turn", turn);

        logger.LogInformation(
            ToolAllowedEvent,
            "Guarded tool allowed. task_id={task_id} tool={tool} target={target} turn={turn}",
            taskId,
            tool,
            target,
            turn);
    }

    public static void LogToolDenied(ILogger logger, string taskId, string tool, string target, string reason, int turn)
    {
        using var span = StartLogEventSpan(ToolDeniedEvent.Name ?? "ingest.tool.denied", "Warning");
        span?.SetTag("task_id", taskId);
        span?.SetTag("tool", tool);
        span?.SetTag("target", target);
        span?.SetTag("reason", reason);
        span?.SetTag("turn", turn);

        logger.LogWarning(
            ToolDeniedEvent,
            "Guarded tool denied. task_id={task_id} tool={tool} target={target} reason={reason} turn={turn}",
            taskId,
            tool,
            target,
            reason,
            turn);
    }

    public static void LogRunRolledBack(ILogger logger, string taskId, int pathsRestored, bool restoredOk)
    {
        using var span = StartLogEventSpan(RunRolledBackEvent.Name ?? "ingest.run.rolled_back", "Warning");
        span?.SetTag("task_id", taskId);
        span?.SetTag("paths_restored", pathsRestored);
        span?.SetTag("restored_ok", restoredOk);

        logger.LogWarning(
            RunRolledBackEvent,
            "Run rollback executed. task_id={task_id} paths_restored={paths_restored} restored_ok={restored_ok}",
            taskId,
            pathsRestored,
            restoredOk);
    }

    public static void LogBackstopAppended(ILogger logger, string taskId, string outcome)
    {
        using var span = StartLogEventSpan(BackstopAppendedEvent.Name ?? "ingest.log.backstop_appended", "Warning");
        span?.SetTag("task_id", taskId);
        span?.SetTag("outcome", outcome);

        logger.LogWarning(
            BackstopAppendedEvent,
            "Log backstop appended. task_id={task_id} outcome={outcome}",
            taskId,
            outcome);
    }

    /// <summary>
    /// Emits <c>ingest.agent.completed</c> with the page counts derived from the
    /// journal-backed run outcome (created/updated/superseded split, not a collapsed total).
    /// </summary>
    public static void LogAgentCompleted(
        ILogger logger,
        string taskId,
        int turns,
        WriteJournal journal,
        int denials)
        => LogAgentCompleted(
            logger,
            taskId,
            turns,
            pagesCreated: journal.CreatedPaths.Count,
            pagesUpdated: journal.UpdatedPaths.Count,
            pagesSuperseded: journal.SupersededPaths.Count,
            denials: denials);

    public static void LogAgentCompleted(
        ILogger logger,
        string taskId,
        int turns,
        int pagesCreated,
        int pagesUpdated,
        int pagesSuperseded,
        int denials)
    {
        using var span = StartLogEventSpan(AgentCompletedEvent.Name ?? "ingest.agent.completed", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("turns", turns);
        span?.SetTag("pages_created", pagesCreated);
        span?.SetTag("pages_updated", pagesUpdated);
        span?.SetTag("pages_superseded", pagesSuperseded);
        span?.SetTag("denials", denials);

        logger.LogInformation(
            AgentCompletedEvent,
            "Agent run completed. task_id={task_id} turns={turns} pages_created={pages_created} pages_updated={pages_updated} pages_superseded={pages_superseded} denials={denials}",
            taskId,
            turns,
            pagesCreated,
            pagesUpdated,
            pagesSuperseded,
            denials);
    }

    public static void LogAgentCapExceeded(ILogger logger, string taskId, string cap, int turns)
    {
        using var span = StartLogEventSpan(AgentCapExceededEvent.Name ?? "ingest.agent.cap_exceeded", "Error");
        span?.SetTag("task_id", taskId);
        span?.SetTag("cap", cap);
        span?.SetTag("turns", turns);

        logger.LogError(
            AgentCapExceededEvent,
            "Agent cap exceeded. task_id={task_id} cap={cap} turns={turns}",
            taskId,
            cap,
            turns);
    }

    private static Activity? StartLogEventSpan(string eventName, string level)
    {
        var span = IngestAgentTracing.ActivitySource.StartActivity(eventName);
        span?.SetTag("signal_type", "log");
        span?.SetTag("event_name", eventName);
        span?.SetTag("level", level);
        return span;
    }
}
