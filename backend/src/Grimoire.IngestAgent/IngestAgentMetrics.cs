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

    private static readonly Histogram<double> _durationSeconds =
        Meter.CreateHistogram<double>("wiki.ingest.duration_seconds",
            unit: "s",
            description: "Wall-clock duration of an ingest operation");

    public static void RecordIngest(string outcome, int pagesTouched, double durationSeconds)
    {
        _operationsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        if (pagesTouched > 0)
            _pagesTouchedTotal.Add(pagesTouched, new KeyValuePair<string, object?>("action", "created"));
        _durationSeconds.Record(durationSeconds, new KeyValuePair<string, object?>("outcome", outcome));
    }
}
