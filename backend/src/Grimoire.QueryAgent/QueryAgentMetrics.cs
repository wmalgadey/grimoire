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

    public static void RecordToolCall(string tool, string decision)
    {
        _toolCallsTotal.Add(1,
            new KeyValuePair<string, object?>("tool", tool),
            new KeyValuePair<string, object?>("decision", decision));
    }
}
