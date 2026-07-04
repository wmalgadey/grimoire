using System.Diagnostics.Metrics;

namespace Grimoire.IngestAgent;

public static class IngestAgentMetrics
{
    internal static readonly Meter Meter = new("Grimoire.IngestAgent", "1.0.0");

    private static readonly Counter<long> _operationsTotal =
        Meter.CreateCounter<long>("wiki.ingest.operations_total",
            description: "Number of ingest operations attempted");

    private static readonly Counter<long> _pagesTouchedTotal =
        Meter.CreateCounter<long>("wiki.ingest.pages_touched_total",
            description: "Number of wiki pages created or updated");

    private static readonly Counter<long> _guardrailDeniedTotal =
        Meter.CreateCounter<long>("ingest.guardrail.actions_denied_total",
            description: "Number of autonomous actions denied by guardrails");

    private static readonly Counter<long> _guardrailAllowedTotal =
        Meter.CreateCounter<long>("ingest.guardrail.actions_allowed_total",
            description: "Number of autonomous actions allowed by guardrails");

    private static readonly Counter<long> _instructionsLoadedTotal =
        Meter.CreateCounter<long>("ingest.instructions.load_total",
            description: "Instruction context load outcomes");

    private static readonly Counter<long> _pagesSupersededTotal =
        Meter.CreateCounter<long>("ingest.wiki.pages_superseded_total",
            description: "Number of explicit wiki supersession links written");

    private static readonly Histogram<double> _durationSeconds =
        Meter.CreateHistogram<double>("wiki.ingest.duration_seconds",
            unit: "s",
            description: "Wall-clock duration of an ingest operation");

    public static void RecordIngest(string outcome, int pagesTouched, string pageAction, double durationSeconds)
    {
        _operationsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        if (pagesTouched > 0)
            _pagesTouchedTotal.Add(pagesTouched, new KeyValuePair<string, object?>("action", pageAction));
        _durationSeconds.Record(durationSeconds, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void RecordInstructionLoad(string taskId, string status)
    {
        _instructionsLoadedTotal.Add(1,
            new KeyValuePair<string, object?>("task_id", taskId),
            new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordGuardrailDecision(string taskId, string actionType, bool allowed, string? reasonCode = null)
    {
        if (allowed)
        {
            _guardrailAllowedTotal.Add(1,
                new KeyValuePair<string, object?>("task_id", taskId),
                new KeyValuePair<string, object?>("action_type", actionType));
            return;
        }

        _guardrailDeniedTotal.Add(1,
            new KeyValuePair<string, object?>("task_id", taskId),
            new KeyValuePair<string, object?>("action_type", actionType),
            new KeyValuePair<string, object?>("reason_code", string.IsNullOrWhiteSpace(reasonCode) ? "policy_deny" : reasonCode));
    }

    public static void RecordSupersededPages(string taskId, int count)
    {
        if (count <= 0)
        {
            return;
        }

        _pagesSupersededTotal.Add(count, new KeyValuePair<string, object?>("task_id", taskId));
    }
}
