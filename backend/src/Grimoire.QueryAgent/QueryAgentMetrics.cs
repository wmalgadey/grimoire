using System.Diagnostics.Metrics;

namespace Grimoire.QueryAgent;

/// <summary>
/// Agent-process-side business metrics (plan.md ## Observability > Business Metrics,
/// 008-query-agent). The guarded tool executor and model loop run inside this process,
/// not the Hub, so their metrics are emitted from here — mirrors
/// <c>Grimoire.IngestAgent.IngestAgentMetrics</c>.
/// </summary>
public static class QueryAgentMetrics
{
    internal static readonly Meter Meter = new("Grimoire.QueryAgent", "1.0.0");

    private static readonly Counter<long> _toolCallsTotal =
        Meter.CreateCounter<long>("query.tool_calls_total",
            description: "Guarded tool calls dispatched by the Query agent");

    /// <summary>ADR-015 (012-query-synthesis-writes): plan.md ## Observability > Business Metrics.</summary>
    private static readonly Counter<long> _synthesisPagesCreatedTotal =
        Meter.CreateCounter<long>("wiki.query.synthesis_pages_created_total",
            description: "Synthesis Pages successfully created by a Query turn");

    /// <summary>T034 (012-query-synthesis-writes, US2): plan.md ## Observability > Business Metrics.</summary>
    private static readonly Counter<long> _writeConflictRejectionsTotal =
        Meter.CreateCounter<long>("wiki.write_conflict.rejections_total",
            description: "Writes rejected by compare-and-swap (stale read) or create-only check");

    /// <summary>T042 (012-query-synthesis-writes, US3): plan.md ## Observability > Business Metrics.</summary>
    private static readonly Counter<long> _writeLockAcquisitionsTotal =
        Meter.CreateCounter<long>("wiki.write_lock.acquisitions_total",
            description: "Write-coordination lock acquisition attempts");

    /// <summary>T042 (012-query-synthesis-writes, US3): plan.md ## Observability > Business Metrics.</summary>
    private static readonly Histogram<double> _writeLockWaitSeconds =
        Meter.CreateHistogram<double>("wiki.write_lock.wait_seconds",
            unit: "s",
            description: "Time spent waiting to acquire a write-coordination lock");

    public static void RecordToolCall(string tool, string decision)
    {
        _toolCallsTotal.Add(1,
            new KeyValuePair<string, object?>("tool", tool),
            new KeyValuePair<string, object?>("decision", decision));
    }

    public static void RecordSynthesisPageCreated() => _synthesisPagesCreatedTotal.Add(1);

    public static void RecordWriteConflictRejected(string reason)
        => _writeConflictRejectionsTotal.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>T042 (012-query-synthesis-writes, US3): emits the acquisitions counter (labeled
    /// <c>outcome=acquired|timeout</c>) and the wait-time histogram for one write-coordination
    /// lock-acquisition attempt.</summary>
    public static void RecordWriteLockAcquisition(string outcome, double waitSeconds)
    {
        _writeLockAcquisitionsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        _writeLockWaitSeconds.Record(waitSeconds);
    }
}
