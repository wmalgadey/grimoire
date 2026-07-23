using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.Hub;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.QueryAgent;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T045 (US1, mirrors IngestLifecycleTraceTests.cs) — validates span names, parent/child
/// linkage, and turn_id correlation for the Query trace spans declared in plan.md
/// ## Observability > Distributed Trace Spans (008-query-agent), split into the Hub-side
/// subtree (submit → spawn_agent → run_supervision → handle_run_event → publish_update)
/// and the agent-process-side subtree (run → load_instructions/model_turn/tool_call/
/// finalize_artifact), since the two are separate processes/ActivitySources in production.
/// </summary>
public class QueryLifecycleTraceTests
{
    [Fact]
    public async Task HubQuerySpans_EmitExpectedHierarchy_ForOneCompletedTurn()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var launcher = new FakeAgentProcessLauncher(autoPlay: true)
        {
            ScriptedAnswerChunks = [("hello ", TimeSpan.Zero), ("world", TimeSpan.Zero)],
        };
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var coordinator = host.Services.GetRequiredService<Grimoire.Hub.QueryDispatch.QueryRunCoordinator>();

        var submission = await coordinator.SubmitTurnAsync("c-trace", 1, "What decisions exist?", []);
        var accepted = Assert.IsType<Grimoire.Hub.QueryDispatch.QuerySubmissionResult.Accepted>(submission);
        var turnId = accepted.Turn.TurnId;

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline &&
               coordinator.GetTurn(turnId)?.Status != Grimoire.Hub.QueryDispatch.QueryTurnStatus.Completed)
        {
            await Task.Delay(25);
        }

        Assert.Equal(Grimoire.Hub.QueryDispatch.QueryTurnStatus.Completed, coordinator.GetTurn(turnId)!.Status);

        var submit = Assert.Single(activities.Where(a => a.OperationName == "hub.query.submit" && GetTag(a, "turn_id") == turnId));
        var spawn = Assert.Single(activities.Where(a => a.OperationName == "hub.query.spawn_agent" && GetTag(a, "turn_id") == turnId));
        var supervision = Assert.Single(activities.Where(a => a.OperationName == "hub.query.run_supervision" && GetTag(a, "turn_id") == turnId));
        var handleEvents = activities.Where(a => a.OperationName == "hub.query.handle_run_event" && GetTag(a, "turn_id") == turnId).ToList();
        var publishUpdates = activities.Where(a => a.OperationName == "hub.query_lifecycle.publish_update" && GetTag(a, "turn_id") == turnId).ToList();

        Assert.Equal(submit.SpanId.ToHexString(), spawn.ParentSpanId.ToHexString());
        Assert.NotEmpty(handleEvents);
        Assert.All(handleEvents, a => Assert.Equal(supervision.SpanId.ToHexString(), a.ParentSpanId.ToHexString()));
        Assert.NotEmpty(publishUpdates);
        Assert.Contains(publishUpdates, a => GetTag(a, "stage") == "answer_chunk");
        Assert.Contains(publishUpdates, a => GetTag(a, "stage") == "completed");
    }

    [Fact]
    public async Task QueryAgentSpans_EmitExpectedHierarchyAndAttributes()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.QueryAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"query-agent-trace-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        var pagesDir = Path.Combine(wikiDir, "pages");
        Directory.CreateDirectory(pagesDir);
        await File.WriteAllTextAsync(Path.Combine(pagesDir, "adr.md"), "# ADR notes");

        var policy = new SafetyPolicy(wikiDir, readPrefixes: [pagesDir + Path.DirectorySeparatorChar], writePrefixes: []);
        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(
            policy, journal, wikiDir, taskId: "turn-trace-1",
            registry: Grimoire.QueryAgent.QueryToolRegistry.Default,
            instrumentation: new QueryToolCallInstrumentation(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
        var fakeModel = new FakeModelClient([
            FakeModelClient.ReadFileTurn("tool-1", "pages/adr.md"),
            FakeModelClient.FinalTurn("The wiki covers ADR notes.")]);
        var loop = new AgentLoop(
            fakeModel, executor,
            registry: Grimoire.QueryAgent.QueryToolRegistry.Default,
            instrumentation: new QueryAgentLoopInstrumentation());

        using (var runSpan = QueryAgentTracing.StartRunActivity("turn-trace-1"))
        {
            using (var loadSpan = QueryAgentTracing.ActivitySource.StartActivity("query_agent.load_instructions"))
            {
                loadSpan?.SetTag("turn_id", "turn-trace-1");
                loadSpan?.SetTag("system_prompt_sha256", "abc123");
            }

            await loop.RunAsync("You are a test query agent.", [new ConversationMessage("user", "What does the wiki cover?")],
                "turn-trace-1", CancellationToken.None);

            using (var finalizeSpan = QueryAgentTracing.ActivitySource.StartActivity("query_agent.finalize_artifact"))
            {
                finalizeSpan?.SetTag("turn_id", "turn-trace-1");
                finalizeSpan?.SetTag("outcome", "completed");
            }
        }

        var run = Assert.Single(activities.Where(a => a.OperationName == "query_agent.run"));
        var all = activities.Where(a => a.TraceId == run.TraceId).ToList();
        var load = Assert.Single(all.Where(a => a.OperationName == "query_agent.load_instructions"));
        var turns = all.Where(a => a.OperationName == "query_agent.model_turn").ToList();
        var tool = Assert.Single(all.Where(a => a.OperationName == "query_agent.tool_call"));
        var finalize = Assert.Single(all.Where(a => a.OperationName == "query_agent.finalize_artifact"));

        Assert.Equal(2, turns.Count);
        Assert.Equal(run.SpanId.ToHexString(), load.ParentSpanId.ToHexString());
        Assert.All(turns, a => Assert.Equal(run.SpanId.ToHexString(), a.ParentSpanId.ToHexString()));
        var toolTurn = turns.Single(t => GetTag(t, "stop_reason") == "tool_use");
        Assert.Equal(toolTurn.SpanId.ToHexString(), tool.ParentSpanId.ToHexString());
        Assert.Equal(run.SpanId.ToHexString(), finalize.ParentSpanId.ToHexString());
        Assert.Equal("allowed", GetTag(tool, "decision"));
        Assert.Equal("read_file", GetTag(tool, "tool"));
    }

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;
}
