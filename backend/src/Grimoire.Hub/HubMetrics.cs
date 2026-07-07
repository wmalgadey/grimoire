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
}
