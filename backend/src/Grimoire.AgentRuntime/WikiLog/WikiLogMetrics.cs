using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Grimoire.AgentRuntime.WikiLog;

/// <summary>
/// Business metrics for the wiki activity log (plan.md ## Observability > Business
/// Metrics). This component is shared across all agent processes, each of which owns its
/// own frozen <see cref="Meter"/> identity (ADR-005/ADR-013: only the profile's own meter
/// name is registered with the OTel <c>MeterProvider</c> in <c>AgentTelemetryBootstrap</c>)
/// — so the counter is created lazily per calling <see cref="Meter"/> instance instead of
/// owning one itself.
///
/// 025-agent-owned-log (ADR-028): <c>wiki.log.backstop_appended_total</c> is retired with
/// the <c>WikiLogAppender</c> backstop, and replaced by
/// <c>wiki.log.unlogged_change_total</c> — which counts the diagnostic the backstop used to
/// paper over rather than the papering-over itself.
/// </summary>
public static class WikiLogMetrics
{
    private const string MetricName = "wiki.log.unlogged_change_total";

    private static readonly ConcurrentDictionary<Meter, Counter<long>> Counters = new();

    /// <summary>
    /// Increments <c>wiki.log.unlogged_change_total</c>, labeled <c>type</c> — a run whose
    /// allowed wiki-content writes were non-zero ended without the activity log among its
    /// writes (FR-012a, SC-009). Never accompanied by a wiki write.
    /// </summary>
    public static void RecordUnloggedChange(Meter meter, string type)
    {
        var counter = Counters.GetOrAdd(meter, static m => m.CreateCounter<long>(
            MetricName,
            description: "Runs that changed wiki content but wrote no activity-log entry, by agent type (ingest/query)"));

        counter.Add(1, new KeyValuePair<string, object?>("type", type));
    }

    private const string FormatDeviationMetricName = "wiki.log.format_deviation_total";

    private static readonly ConcurrentDictionary<Meter, Counter<long>> FormatDeviationCounters = new();

    /// <summary>
    /// 028-lint-at-scale (US3, FR-016, SC-009, Clarifications 2026-08-27): increments
    /// <c>wiki.log.format_deviation_total</c>, labeled <c>agent</c>/<c>mode</c>/<c>reason</c>
    /// — a <c>log.md</c> write committed despite deviating from the activity-log format
    /// contract's expected shape (contracts/log-prepend-write.md). Never called for a
    /// conforming write. <paramref name="reason"/> is the comma-joined reason code(s), for
    /// a write that carried more than one.
    /// </summary>
    public static void RecordFormatDeviation(Meter meter, string agent, string mode, string reason)
    {
        var counter = FormatDeviationCounters.GetOrAdd(meter, static m => m.CreateCounter<long>(
            FormatDeviationMetricName,
            description: "A log.md write committed despite deviating from the activity-log format contract's expected shape"));

        counter.Add(1,
            new KeyValuePair<string, object?>("agent", agent),
            new KeyValuePair<string, object?>("mode", mode),
            new KeyValuePair<string, object?>("reason", reason));
    }
}
