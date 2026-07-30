using System.Diagnostics.Metrics;

namespace Grimoire.LintAgent;

/// <summary>
/// Agent-process-side business metrics. The guarded tool executor and model loop run
/// inside this process, not the Hub, so their metrics are emitted from here — mirrors
/// <c>Grimoire.QueryAgent.QueryAgentMetrics</c>. The Hub-side <c>wiki.lint.*</c> metrics
/// declared in plan.md ## Observability (runs_total, findings_total,
/// inbound_links_refreshed_total, triggers_rejected_total) are emitted from
/// <c>Grimoire.Hub.LintDispatch</c> instead, since those are dispatch-lifecycle/
/// report-derived facts the Hub already owns.
/// </summary>
public static class LintAgentMetrics
{
    internal static readonly Meter Meter = new("Grimoire.LintAgent", "1.0.0");

    private static readonly Counter<long> _toolCallsTotal =
        Meter.CreateCounter<long>("lint.tool_calls_total",
            description: "Guarded tool calls dispatched by the Lint agent");

    /// <summary>ADR-015/ADR-016: reuses the shared write-conflict signal (plan.md's
    /// Observability note) — writes rejected by the write-coordination guard's
    /// existence/compare-and-swap/frontmatter-body checks.</summary>
    private static readonly Counter<long> _writeConflictRejectionsTotal =
        Meter.CreateCounter<long>("wiki.write_conflict.rejections_total",
            description: "Writes rejected by compare-and-swap (stale read), create-only, or frontmatter-only checks");

    private static readonly Counter<long> _writeLockAcquisitionsTotal =
        Meter.CreateCounter<long>("wiki.write_lock.acquisitions_total",
            description: "Write-coordination lock acquisition attempts");

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

    public static void RecordWriteConflictRejected(string reason)
        => _writeConflictRejectionsTotal.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public static void RecordWriteLockAcquisition(string outcome, double waitSeconds)
    {
        _writeLockAcquisitionsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        _writeLockWaitSeconds.Record(waitSeconds);
    }
}
