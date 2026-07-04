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

    public static void RecordIngest(string outcome, int pagesTouched, string pageAction, double durationSeconds)
    {
        _operationsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        if (pagesTouched > 0)
            _pagesTouchedTotal.Add(pagesTouched, new KeyValuePair<string, object?>("action", pageAction));
        _durationSeconds.Record(durationSeconds, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void RecordPageTouched(string action)
        => _pagesTouchedTotal.Add(1, new KeyValuePair<string, object?>("action", action));

    public static void RecordAgentTurns(int turns, string outcome)
        => _agentTurnsTotal.Add(turns,
            new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordToolCall(string tool, string decision)
        => _toolCallsTotal.Add(1,
            new KeyValuePair<string, object?>("tool", tool),
            new KeyValuePair<string, object?>("decision", decision));

    public static void RecordActionDenied(string tool, string reason)
        => _actionsDeniedTotal.Add(1,
            new KeyValuePair<string, object?>("tool", tool),
            new KeyValuePair<string, object?>("reason", reason));

    public static void RecordRollback(bool restoredOk)
        => _runsRolledBackTotal.Add(1,
            new KeyValuePair<string, object?>("restored_ok", restoredOk ? "true" : "false"));

    public static void RecordInstructionLoadFailure(string artifact)
        => _instructionLoadFailuresTotal.Add(1,
            new KeyValuePair<string, object?>("artifact", artifact));

    public static void RecordModelTokens(int inputTokens, int outputTokens)
    {
        _modelTokensTotal.Add(inputTokens, new KeyValuePair<string, object?>("direction", "input"));
        _modelTokensTotal.Add(outputTokens, new KeyValuePair<string, object?>("direction", "output"));
    }
}
