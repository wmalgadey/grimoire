using System.Diagnostics;
using System.Diagnostics.Metrics;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;

namespace Grimoire.IngestAgent;

public static class IngestAgentMetrics
{
    internal static readonly Meter Meter = new("Grimoire.IngestAgent", "1.0.0");

    private static readonly Counter<long> _operationsTotal =
        Meter.CreateCounter<long>("wiki.ingest.operations_total",
            description: "Number of ingest operations attempted");

    private static readonly Counter<long> _pagesTouchedTotal =
        Meter.CreateCounter<long>("wiki.ingest.pages_touched_total",
            description: "Number of wiki pages created, updated, or superseded");

    private static readonly Histogram<double> _durationSeconds =
        Meter.CreateHistogram<double>("wiki.ingest.duration_seconds",
            unit: "s",
            description: "Wall-clock duration of an ingest operation");

    private static readonly Counter<long> _agentTurnsTotal =
        Meter.CreateCounter<long>("wiki.ingest.agent_turns_total",
            description: "Model turns consumed per run");

    private static readonly Counter<long> _toolCallsTotal =
        Meter.CreateCounter<long>("wiki.ingest.tool_calls_total",
            description: "Guarded tool invocations");

    private static readonly Counter<long> _actionsDeniedTotal =
        Meter.CreateCounter<long>("wiki.ingest.actions_denied_total",
            description: "Policy denials");

    private static readonly Counter<long> _runsRolledBackTotal =
        Meter.CreateCounter<long>("wiki.ingest.runs_rolled_back_total",
            description: "Failed runs whose journal rollback executed");

    private static readonly Counter<long> _instructionLoadFailuresTotal =
        Meter.CreateCounter<long>("wiki.ingest.instruction_load_failures_total",
            description: "Runs aborted because instructions or policy could not load");

    private static readonly Counter<long> _modelTokensTotal =
        Meter.CreateCounter<long>("wiki.ingest.model_tokens_total",
            description: "Input/output tokens reported by the API");

    private static readonly Counter<long> _modelToolRequestsTotal =
        Meter.CreateCounter<long>("wiki.ingest.model_tool_requests_total",
            description: "Number of tool requests returned by model turns");

    private static readonly Counter<long> _noToolTurnsTotal =
        Meter.CreateCounter<long>("wiki.ingest.no_tool_turns_total",
            description: "Model turns with zero tool requests, labeled by stop reason and loop outcome");

    public static void RecordIngest(string outcome, double durationSeconds)
    {
        using var span = StartMetricSpan("wiki.ingest.operations_total");
        span?.SetTag("outcome", outcome);

        _operationsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        _durationSeconds.Record(durationSeconds, new KeyValuePair<string, object?>("outcome", outcome));
    }

    /// <summary>
    /// Records <c>wiki.ingest.pages_touched_total</c> with the plan-mandated
    /// <c>action=created|updated|superseded</c> split derived from the
    /// journal-backed run outcome.
    /// </summary>
    public static void RecordPagesTouched(WriteJournal journal)
    {
        RecordPagesTouched("created", journal.CreatedPaths.Count);
        RecordPagesTouched("updated", journal.UpdatedPaths.Count);
        RecordPagesTouched("superseded", journal.SupersededPaths.Count);
    }

    public static void RecordPagesTouched(string action, int count)
    {
        if (count <= 0)
            return;

        using var span = StartMetricSpan("wiki.ingest.pages_touched_total");
        span?.SetTag("action", action);
        span?.SetTag("count", count);

        _pagesTouchedTotal.Add(count, new KeyValuePair<string, object?>("action", action));
    }

    public static void RecordAgentTurns(int turns, string outcome)
    {
        using var span = StartMetricSpan("wiki.ingest.agent_turns_total");
        span?.SetTag("turns", turns);
        span?.SetTag("outcome", outcome);

        _agentTurnsTotal.Add(turns,
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void RecordToolCall(string tool, string decision)
    {
        using var span = StartMetricSpan("wiki.ingest.tool_calls_total");
        span?.SetTag("tool", tool);
        span?.SetTag("decision", decision);

        _toolCallsTotal.Add(1,
            new KeyValuePair<string, object?>("tool", tool),
            new KeyValuePair<string, object?>("decision", decision));
    }

    public static void RecordActionDenied(string tool, string reason)
    {
        using var span = StartMetricSpan("wiki.ingest.actions_denied_total");
        span?.SetTag("tool", tool);
        span?.SetTag("reason", reason);

        _actionsDeniedTotal.Add(1,
            new KeyValuePair<string, object?>("tool", tool),
            new KeyValuePair<string, object?>("reason", reason));
    }

    public static void RecordRollback(bool restoredOk)
    {
        using var span = StartMetricSpan("wiki.ingest.runs_rolled_back_total");
        span?.SetTag("restored_ok", restoredOk);

        _runsRolledBackTotal.Add(1,
            new KeyValuePair<string, object?>("restored_ok", restoredOk ? "true" : "false"));
    }

    public static void RecordInstructionLoadFailure(string artifact)
    {
        using var span = StartMetricSpan("wiki.ingest.instruction_load_failures_total");
        span?.SetTag("artifact", artifact);

        _instructionLoadFailuresTotal.Add(1,
            new KeyValuePair<string, object?>("artifact", artifact));
    }

    public static void RecordModelTokens(int inputTokens, int outputTokens)
    {
        using var span = StartMetricSpan("wiki.ingest.model_tokens_total");
        span?.SetTag("input_tokens", inputTokens);
        span?.SetTag("output_tokens", outputTokens);

        _modelTokensTotal.Add(inputTokens, new KeyValuePair<string, object?>("direction", "input"));
        _modelTokensTotal.Add(outputTokens, new KeyValuePair<string, object?>("direction", "output"));
    }

    public static void RecordModelToolRequests(int toolRequestCount, ModelStopReason stopReason)
    {
        using var span = StartMetricSpan("wiki.ingest.model_tool_requests_total");
        span?.SetTag("tool_request_count", toolRequestCount);
        span?.SetTag("stop_reason", stopReason.ToProtocolString());

        _modelToolRequestsTotal.Add(toolRequestCount,
            new KeyValuePair<string, object?>("stop_reason", stopReason.ToProtocolString()));
    }

    public static void RecordNoToolTurn(ModelStopReason stopReason, string outcome)
    {
        using var span = StartMetricSpan("wiki.ingest.no_tool_turns_total");
        span?.SetTag("stop_reason", stopReason.ToProtocolString());
        span?.SetTag("outcome", outcome);

        _noToolTurnsTotal.Add(1,
            new KeyValuePair<string, object?>("stop_reason", stopReason.ToProtocolString()),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    private static Activity? StartMetricSpan(string metricName)
    {
        var span = IngestAgentTracing.ActivitySource.StartActivity(metricName);
        span?.SetTag("signal_type", "metric");
        span?.SetTag("metric_name", metricName);
        return span;
    }
}
