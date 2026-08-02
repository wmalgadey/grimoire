using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Grimoire.Hub;

public static class HubMetrics
{
    internal static readonly Meter Meter = new("Grimoire.Hub", "1.0.0");

    private static readonly Counter<long> _tasksReconciledTotal =
        Meter.CreateCounter<long>("wiki.ingest.tasks_reconciled_total",
            description: "Number of running tasks reconciled to failed on Hub restart");

    public static void RecordTaskReconciled()
    {
        using var span = HubTracing.ActivitySource.StartActivity("wiki.ingest.tasks_reconciled_total");
        span?.SetTag("signal_type", "metric");
        span?.SetTag("metric_name", "wiki.ingest.tasks_reconciled_total");

        _tasksReconciledTotal.Add(1);
    }

    // --- 003-ingest-intake-webui (plan.md ## Observability > Business Metrics) ---

    private static readonly Counter<long> _ingestSubmissionsTotal =
        Meter.CreateCounter<long>("hub.ingest_submissions_total",
            description: "Accepted/rejected ingest-submission requests");

    public static void RecordIngestSubmission(string kind, string outcome)
    {
        _ingestSubmissionsTotal.Add(1,
            new KeyValuePair<string, object?>("kind", kind),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    private static readonly Counter<long> _ingestSubmissionConversionsTotal =
        Meter.CreateCounter<long>("hub.ingest_submission_conversions_total",
            description: "Ingest-submission conversion outcomes");

    public static void RecordIngestSubmissionConversion(string kind, string outcome)
    {
        _ingestSubmissionConversionsTotal.Add(1,
            new KeyValuePair<string, object?>("kind", kind),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    private static readonly Counter<long> _ingestSubmissionUrlFetchTotal =
        Meter.CreateCounter<long>("hub.ingest_submission_url_fetch_total",
            description: "URL fetch attempts in ingest submission");

    public static void RecordIngestSubmissionUrlFetch(string outcome, string? failureType)
    {
        _ingestSubmissionUrlFetchTotal.Add(1,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("failure_type", failureType));
    }

    private static readonly Counter<long> _ingestSubmissionArtifactsPersistedTotal =
        Meter.CreateCounter<long>("hub.ingest_submission_artifacts_persisted_total",
            description: "Stored artifacts by type");

    public static void RecordIngestSubmissionArtifactPersisted(string artifact)
    {
        _ingestSubmissionArtifactsPersistedTotal.Add(1, new KeyValuePair<string, object?>("artifact", artifact));
    }

    private static readonly Gauge<double> _ingestSubmissionQueueWaitSeconds =
        Meter.CreateGauge<double>("hub.ingest_submission_queue_wait_seconds",
            description: "Waiting time in queued before ingest run starts");

    public static void RecordIngestSubmissionQueueWait(string taskId, double seconds)
    {
        _ingestSubmissionQueueWaitSeconds.Record(seconds, new KeyValuePair<string, object?>("task_id", taskId));
    }

    // --- 004-ingest-agent-systemprompt (plan.md ## Observability > Business Metrics) ---

    private static readonly Counter<long> _userPromptTotal =
        Meter.CreateCounter<long>("wiki.ingest.user_prompt_total",
            description: "Accepted submissions by prompt origin");

    public static void RecordUserPrompt(string source)
    {
        _userPromptTotal.Add(1, new KeyValuePair<string, object?>("source", source));
    }

    private static readonly Counter<long> _convertStepDisabledTotal =
        Meter.CreateCounter<long>("wiki.ingest.convert_step_disabled_total",
            description: "Accepted submissions that disabled a convert step");

    public static void RecordConvertStepDisabled(string step)
    {
        _convertStepDisabledTotal.Add(1, new KeyValuePair<string, object?>("step", step));
    }

    private static readonly Counter<long> _runEventsTotal =
        Meter.CreateCounter<long>("wiki.ingest.run_events_total",
            description: "Agent Run Events received by the Hub");

    public static void RecordRunEvent(string eventType)
    {
        _runEventsTotal.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
    }

    private static readonly Counter<long> _livenessFailuresTotal =
        Meter.CreateCounter<long>("wiki.ingest.liveness_failures_total",
            description: "Runs failed by liveness-window expiry");

    public static void RecordLivenessFailure()
    {
        _livenessFailuresTotal.Add(1);
    }

    private static readonly Gauge<long> _queueDepth =
        Meter.CreateGauge<long>("wiki.ingest.queue_depth",
            description: "Tasks currently waiting in the Run Queue");

    public static void RecordQueueDepth(long depth)
    {
        _queueDepth.Record(depth);
    }

    // --- 006-hexagonal-arch-tasks-ui (plan.md ## Observability > Business Metrics) ---

    private static readonly Counter<long> _taskRecordReadsTotal =
        Meter.CreateCounter<long>("hub.task_record_reads_total",
            description: "Task-record API reads");

    public static void RecordTaskRecordRead(string outcome)
    {
        _taskRecordReadsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    private static readonly Counter<long> _taskRecordChangeEventsTotal =
        Meter.CreateCounter<long>("hub.task_record_change_events_total",
            description: "taskRecordChanged events published");

    public static void RecordTaskRecordChangeEvent()
    {
        _taskRecordChangeEventsTotal.Add(1);
    }

    // --- 008-query-agent (plan.md ## Observability > Business Metrics) ---

    private static readonly Counter<long> _queryTurnsTotal =
        Meter.CreateCounter<long>("query.turns_total",
            description: "Query Turns reaching a terminal state");

    private static readonly Histogram<double> _queryTurnDurationSeconds =
        Meter.CreateHistogram<double>("query.turn_duration_seconds",
            unit: "s",
            description: "Wall-clock duration of a Query Turn");

    public static void RecordQueryTurn(string outcome, double durationSeconds)
    {
        _queryTurnsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        _queryTurnDurationSeconds.Record(durationSeconds, new KeyValuePair<string, object?>("outcome", outcome));
    }

    private static readonly Counter<long> _queryAnswerChunksTotal =
        Meter.CreateCounter<long>("query.answer_chunks_total",
            description: "answer_chunk events emitted");

    public static void RecordQueryAnswerChunk()
    {
        _queryAnswerChunksTotal.Add(1);
    }

    private static readonly Counter<long> _querySubmissionsRejectedTotal =
        Meter.CreateCounter<long>("query.submissions_rejected_total",
            description: "Submissions rejected for being over the concurrency limit");

    public static void RecordQuerySubmissionRejected()
    {
        _querySubmissionsRejectedTotal.Add(1);
    }

    private static readonly UpDownCounter<long> _queryConcurrentRuns =
        Meter.CreateUpDownCounter<long>("query.concurrent_runs",
            description: "Currently running Query Turns");

    /// <summary>
    /// Adjusts the live count of non-terminal Query Turns (T080 gap fix: this metric row
    /// existed in plan.md but had no implementation). Called once per turn creation
    /// (+1, in <c>QueryRunCoordinator.SubmitTurnAsync</c>) and exactly once per turn's
    /// first terminal transition (-1, in <c>FinishTurnAsync</c>) — symmetric by
    /// construction via the same idempotent first-transition-wins guard everything else
    /// in that class uses, so this can never drift negative or double-count.
    /// </summary>
    public static void AdjustQueryConcurrentRuns(long delta)
    {
        _queryConcurrentRuns.Add(delta);
    }

    // --- 011-query-conversations (plan.md ## Observability > Business Metrics) ---

    private static readonly Counter<long> _conversationTurnsRecordedTotal =
        Meter.CreateCounter<long>("query.conversation.turns_recorded_total",
            description: "Turns appended to a Conversation Record");

    public static void RecordConversationTurnRecorded(string outcome)
    {
        _conversationTurnsRecordedTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    private static readonly Counter<long> _conversationRecordAppendFailuresTotal =
        Meter.CreateCounter<long>("query.conversation.record_append_failures_total",
            description: "Failed record appends (turn outcome unaffected)");

    public static void RecordConversationRecordAppendFailure()
    {
        _conversationRecordAppendFailuresTotal.Add(1);
    }

    private static readonly Counter<long> _conversationContextLoadsTotal =
        Meter.CreateCounter<long>("query.conversation.context_loads_total",
            description: "Prior-turn context loads at submission");

    public static void RecordConversationContextLoad(string source)
    {
        _conversationContextLoadsTotal.Add(1, new KeyValuePair<string, object?>("source", source));
    }

    private static readonly Counter<long> _conversationRecordLoadFailuresTotal =
        Meter.CreateCounter<long>("query.conversation.record_load_failures_total",
            description: "Fail-closed context loads (unreadable record)");

    public static void RecordConversationRecordLoadFailure()
    {
        _conversationRecordLoadFailuresTotal.Add(1);
    }

    // Note: query.tool_calls_total is emitted by Grimoire.QueryAgent itself (the guarded
    // tool executor runs in that process, not the Hub) — see QueryAgentMetrics there,
    // mirroring Grimoire.IngestAgent.IngestAgentMetrics.RecordToolCall.

    // --- 013-lint-agent (plan.md ## Observability > Business Metrics) ---

    private static readonly Counter<long> _lintRunsTotal =
        Meter.CreateCounter<long>("wiki.lint.runs_total",
            description: "Lint Runs reaching a terminal state");

    public static void RecordLintRun(string outcome)
        => _lintRunsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>T027 gap-fill: plan.md declares this metric but tasks.md assigns it to no
    /// specific task (T027 names only wiki.lint.runs_total; T037 names only
    /// findings_total/inbound_links_refreshed_total) — emitted here, at the one call site
    /// that observes a rejection, rather than left for the Phase 6 completeness audit to
    /// discover as a gap.</summary>
    private static readonly Counter<long> _lintTriggersRejectedTotal =
        Meter.CreateCounter<long>("wiki.lint.triggers_rejected_total",
            description: "Lint Run trigger attempts rejected because a run was already active");

    public static void RecordLintTriggerRejected() => _lintTriggersRejectedTotal.Add(1);

    /// <summary>T037 (013-lint-agent, US2): findings tallied per category, mechanically
    /// counted from the agent's own narrative headings (<c>FindingsNarrativeStats</c>) —
    /// never a judgment about what counts as a finding (Constitution Principle V).</summary>
    private static readonly Counter<long> _lintFindingsTotal =
        Meter.CreateCounter<long>("wiki.lint.findings_total",
            description: "Findings produced across all runs, by category");

    public static void RecordLintFindings(string category, int count)
        => _lintFindingsTotal.Add(count, new KeyValuePair<string, object?>("category", category));

    /// <summary>T037 (013-lint-agent, US2): pages whose <c>inbound_links</c> frontmatter
    /// was refreshed this run — sourced from the harness's own write journal
    /// (<c>GuardedToolExecutor.TouchedPaths</c> via the run's terminal event), since
    /// ADR-016's policy has exactly one write rule.</summary>
    private static readonly Counter<long> _lintInboundLinksRefreshedTotal =
        Meter.CreateCounter<long>("wiki.lint.inbound_links_refreshed_total",
            description: "Pages whose inbound-link count was updated");

    public static void RecordLintInboundLinksRefreshed(int count)
        => _lintInboundLinksRefreshedTotal.Add(count);

    // --- 015-lint-board-parity (plan.md ## Observability > Business Metrics) ---

    /// <summary>T022 (US3, FR-007): one increment per remediation action task the Hub
    /// materialized from a lint run's <c>proposedActions</c> — counting only; the
    /// proposal content is agent-authored and harness-opaque (Principle V).</summary>
    private static readonly Counter<long> _remediationTasksProposedTotal =
        Meter.CreateCounter<long>("wiki.lint.remediation_tasks_proposed_total",
            description: "Remediation action tasks proposed by a lint run's findings assessment");

    public static void RecordRemediationTaskProposed(string runId)
        => _remediationTasksProposedTotal.Add(1, new KeyValuePair<string, object?>("run_id", runId));

    /// <summary>T023 (US3): one increment per <c>remediationTaskLifecycleChanged</c>
    /// broadcast, tagged with the destination stage — sibling of
    /// <c>hub.lint_lifecycle_updates_total</c>/<c>hub.ingest_lifecycle_updates_total</c>.</summary>
    private static readonly Counter<long> _remediationLifecycleUpdatesTotal =
        Meter.CreateCounter<long>("hub.remediation_lifecycle_updates_total",
            description: "Realtime remediation task lifecycle events published");

    public static void RecordRemediationLifecycleUpdate(string stage)
        => _remediationLifecycleUpdatesTotal.Add(1, new KeyValuePair<string, object?>("stage", stage));

    /// <summary>T033 (US4, FR-009): one increment per task the authorize endpoint moves proposed → authorized.</summary>
    private static readonly Counter<long> _remediationTasksAuthorizedTotal =
        Meter.CreateCounter<long>("wiki.remediation.tasks_authorized_total",
            description: "Remediation action tasks authorized by a human");

    public static void RecordRemediationTaskAuthorized() => _remediationTasksAuthorizedTotal.Add(1);

    /// <summary>T033 (US4, FR-010): one increment per task the dismiss endpoint resolves without execution.</summary>
    private static readonly Counter<long> _remediationTasksDismissedTotal =
        Meter.CreateCounter<long>("wiki.remediation.tasks_dismissed_total",
            description: "Remediation action tasks dismissed without execution");

    public static void RecordRemediationTaskDismissed() => _remediationTasksDismissedTotal.Add(1);

    /// <summary>T033 (US4, FR-016): one increment per authorization withdrawn before execution started.</summary>
    private static readonly Counter<long> _remediationTasksWithdrawnTotal =
        Meter.CreateCounter<long>("wiki.remediation.tasks_withdrawn_total",
            description: "Authorizations withdrawn before execution started");

    public static void RecordRemediationTaskWithdrawn() => _remediationTasksWithdrawnTotal.Add(1);

    /// <summary>T032 (US4): one increment per remediation execution reaching a terminal outcome.</summary>
    private static readonly Counter<long> _remediationTasksExecutedTotal =
        Meter.CreateCounter<long>("wiki.remediation.tasks_executed_total",
            description: "Remediation action tasks reaching a terminal execution outcome");

    public static void RecordRemediationTaskExecuted(string outcome)
        => _remediationTasksExecutedTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>T032 (US4, FR-017): remediation action tasks currently queued/waiting to execute.</summary>
    private static readonly Gauge<long> _remediationQueueDepth =
        Meter.CreateGauge<long>("wiki.remediation.queue_depth",
            description: "Remediation action tasks currently queued/waiting to execute");

    public static void RecordRemediationQueueDepth(long depth) => _remediationQueueDepth.Record(depth);

    /// <summary>T041 (US5, FR-012): one increment per completed message turn, tagged with its outcome.</summary>
    private static readonly Counter<long> _remediationMessageTurnsTotal =
        Meter.CreateCounter<long>("hub.remediation.message_turns_total",
            description: "Task-message agent turns completed");

    public static void RecordRemediationMessageTurn(string outcome)
        => _remediationMessageTurnsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
