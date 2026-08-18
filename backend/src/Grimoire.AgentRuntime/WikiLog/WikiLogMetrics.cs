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
}
