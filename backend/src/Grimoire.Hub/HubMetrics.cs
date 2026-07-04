using System.Diagnostics.Metrics;

namespace Grimoire.Hub;

public static class HubMetrics
{
    internal static readonly Meter Meter = new("Grimoire.Hub", "1.0.0");

    private static readonly Counter<long> _tasksReconciledTotal =
        Meter.CreateCounter<long>("wiki.ingest.tasks_reconciled_total",
            description: "Number of running tasks reconciled to failed on Hub restart");

    public static void RecordTaskReconciled() => _tasksReconciledTotal.Add(1);
}
