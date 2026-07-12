using System.Diagnostics;
using Grimoire.Hub;
using Grimoire.Hub.AgentDispatch;
using Grimoire.IngestAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T068 (Convergence) - the Hub's `hub.ingest_run.trigger` span and the dispatched Ingest agent's
/// `ingest_agent.run` span must form a single end-to-end trace, not two disconnected trees
/// (Constitution IV: "distributed trace spans MUST be implemented as an end-to-end, observable
/// trace chain in code").
/// </summary>
public class TraceContextPropagationTests
{
    [Fact]
    public void BuildChildEnvironment_PropagatesTraceParent_FromCurrentHubActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var triggerSpan = HubTracing.ActivitySource.StartActivity("hub.ingest_run.trigger");
        Assert.NotNull(triggerSpan);

        var childEnv = AgentProcessHost.BuildChildEnvironment(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            authToken: null,
            currentActivity: triggerSpan);

        Assert.True(childEnv.TryGetValue("TRACEPARENT", out var traceparent));
        Assert.Equal($"00-{triggerSpan!.TraceId}-{triggerSpan.SpanId}-01", traceparent);
    }

    [Fact]
    public void BuildChildEnvironment_OmitsTraceParent_WhenNoCurrentActivity()
    {
        var childEnv = AgentProcessHost.BuildChildEnvironment(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            authToken: null,
            currentActivity: null);

        Assert.False(childEnv.ContainsKey("TRACEPARENT"));
    }

    /// <summary>
    /// T076 (Convergence) - an unsampled (not Recorded) parent must not be propagated: a
    /// `00`-flagged TRACEPARENT makes the agent's ParentBased sampler drop `ingest_agent.run`
    /// entirely (StartRunActivity returns null), fragmenting the rest of the run into disconnected
    /// per-turn root traces instead of a single end-to-end trace. Built via the legacy `new
    /// Activity(...)` constructor (not `ActivitySource.StartActivity`) so the Recorded flag is
    /// deterministic and unaffected by other test classes' global `ActivitySource.AddActivityListener`
    /// registrations running concurrently on the same "Grimoire.Hub" source.
    /// </summary>
    [Fact]
    public void BuildChildEnvironment_OmitsTraceParent_WhenCurrentActivityIsNotRecorded()
    {
        using var unsampledActivity = new Activity("hub.ingest_run.trigger") { ActivityTraceFlags = ActivityTraceFlags.None };
        unsampledActivity.Start();
        Assert.False(unsampledActivity.Recorded);

        var childEnv = AgentProcessHost.BuildChildEnvironment(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            authToken: null,
            currentActivity: unsampledActivity);

        Assert.False(childEnv.ContainsKey("TRACEPARENT"));
        Assert.False(childEnv.ContainsKey("TRACESTATE"));
    }

    [Fact]
    public void StartRunActivity_ParentsToPropagatedTraceParent_FromHub()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.IngestAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        Environment.SetEnvironmentVariable("TRACEPARENT", $"00-{traceId}-{spanId}-01");
        try
        {
            using var activity = IngestAgentTracing.StartRunActivity("task-123");

            Assert.NotNull(activity);
            Assert.Equal(traceId, activity!.TraceId);
            Assert.Equal(spanId, activity.ParentSpanId);
            Assert.Equal("task-123", activity.GetTagItem("task_id"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACEPARENT", null);
        }
    }

    [Fact]
    public void StartRunActivity_StartsFreshTrace_WhenNoTraceParentPropagated()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.IngestAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        Environment.SetEnvironmentVariable("TRACEPARENT", null);

        using var activity = IngestAgentTracing.StartRunActivity("task-456");

        Assert.NotNull(activity);
        Assert.Equal(default, activity!.ParentSpanId);
    }
}
