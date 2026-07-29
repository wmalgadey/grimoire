using System.Diagnostics;

namespace Grimoire.AgentRuntime.Telemetry;

/// <summary>
/// ActivitySource holder + run-span start helper for agent host processes (ADR-013;
/// consolidates the formerly duplicated IngestAgentTracing/QueryAgentTracing scaffolds).
/// Source name, run-span name, and correlation-attribute name ("task_id"/"turn_id") are
/// frozen per-agent identities supplied by the host — span shapes are unchanged
/// (research.md R2).
/// </summary>
public sealed class AgentTracing
{
    private readonly string _runSpanName;
    private readonly string _correlationAttribute;

    public AgentTracing(string activitySourceName, string runSpanName, string correlationAttribute)
    {
        ActivitySource = new ActivitySource(activitySourceName, "1.0.0");
        _runSpanName = runSpanName;
        _correlationAttribute = correlationAttribute;
    }

    public ActivitySource ActivitySource { get; }

    /// <summary>
    /// Starts the agent's root run span, parented to the `TRACEPARENT`/`TRACESTATE`
    /// environment variables the Hub propagates when it dispatches the process, so the
    /// Hub's trigger/spawn span and this agent run form a single end-to-end trace
    /// instead of two disconnected trees (Constitution IV).
    /// </summary>
    public Activity? StartRunActivity(string correlationId)
    {
        var traceParent = Environment.GetEnvironmentVariable("TRACEPARENT");
        var traceState = Environment.GetEnvironmentVariable("TRACESTATE");

        var activity = !string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, traceState, out var parentContext)
            ? ActivitySource.StartActivity(_runSpanName, ActivityKind.Internal, parentContext)
            : ActivitySource.StartActivity(_runSpanName);

        activity?.SetTag(_correlationAttribute, correlationId);
        return activity;
    }
}
