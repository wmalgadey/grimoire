using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Grimoire.AgentRuntime.WikiLog;

/// <summary>
/// <c>wiki.log.backstop_appended_total</c> (plan.md ## Observability > Business
/// Metrics), generalizing the formerly Ingest-only signal to every agent type. Unlike a
/// single per-agent static <see cref="Meter"/> (the pre-014 per-agent-process pattern —
/// see e.g. <c>IngestAgentMetrics</c>), this component is shared across all three agent
/// processes (<see cref="WikiLogAppender"/>), each of which owns its own frozen
/// <see cref="Meter"/> identity (ADR-005/ADR-013: only the profile's own meter name is
/// registered with the OTel <c>MeterProvider</c> in <c>AgentTelemetryBootstrap</c>) — so
/// the counter is created lazily per calling <see cref="Meter"/> instance instead of
/// owning one itself.
/// </summary>
public static class WikiLogMetrics
{
    private const string MetricName = "wiki.log.backstop_appended_total";

    private static readonly ConcurrentDictionary<Meter, Counter<long>> Counters = new();

    /// <summary>Increments <c>wiki.log.backstop_appended_total</c>, labeled <c>type</c>.</summary>
    public static void RecordBackstopAppended(Meter meter, string type)
    {
        var counter = Counters.GetOrAdd(meter, static m => m.CreateCounter<long>(
            MetricName,
            description: "Backstop log.md entries appended, generalized across agent types (ingest/query/lint)"));

        counter.Add(1, new KeyValuePair<string, object?>("type", type));
    }
}
