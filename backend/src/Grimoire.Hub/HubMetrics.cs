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

    // Note: query.tool_calls_total is emitted by Grimoire.QueryAgent itself (the guarded
    // tool executor runs in that process, not the Hub) — see QueryAgentMetrics there,
    // mirroring Grimoire.IngestAgent.IngestAgentMetrics.RecordToolCall.
}
