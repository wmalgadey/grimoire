using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T026/T032 (011-query-conversations, mirrors QueryLifecycleTraceTests.cs, in-memory
/// listener per ADR-005) — validates span names, parent/child linkage, and
/// <c>turn_id</c>/<c>conversation_id</c> correlation for the conversation-record spans
/// declared in plan.md ## Observability > Distributed Trace Spans:
/// <c>hub.query.submit</c> → <c>hub.query.load_conversation_context</c>, and
/// <c>hub.query.record_turn</c> parented by <c>hub.query.run_supervision</c> for
/// supervision-detected terminals or by the interrupt HTTP request for user
/// interruption.
/// </summary>
public class QueryConversationTraceTests
{
    private static ActivityListener CreateListener(ConcurrentQueue<Activity> activities, params string[] sources) => new()
    {
        ShouldListenTo = src => sources.Contains(src.Name),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        ActivityStopped = activity => activities.Enqueue(activity),
    };

    [Fact]
    public async Task LoadConversationContextSpan_IsChildOfSubmit_WithConversationTurnCountAndSourceAttributes()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = CreateListener(activities, "Grimoire.Hub");
        ActivitySource.AddActivityListener(listener);

        var launcher = new FakeAgentProcessLauncher(autoPlay: true)
        {
            ScriptedAnswerChunks = [("The answer.", TimeSpan.Zero)],
        };
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var turnId = await QueryConversationRecordLifecycleTests.SubmitAsync(client, "c-trace-load", "What decisions exist?");
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, turnId, "completed");

        var submit = Assert.Single(activities.Where(a => a.OperationName == "hub.query.submit" && GetTag(a, "turn_id") == turnId));
        var load = Assert.Single(activities.Where(a =>
            a.OperationName == "hub.query.load_conversation_context" && GetTag(a, "conversation_id") == "c-trace-load"));

        Assert.Equal(submit.SpanId.ToHexString(), load.ParentSpanId.ToHexString());
        Assert.Equal("c-trace-load", GetTag(load, "conversation_id"));
        Assert.Equal("0", GetTag(load, "turn_count"));
        Assert.Equal("empty", GetTag(load, "source"));
    }

    [Fact]
    public async Task RecordTurnSpan_IsChildOfRunSupervision_ForSupervisionDetectedTerminal_WithCorrelatedAttributes()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = CreateListener(activities, "Grimoire.Hub");
        ActivitySource.AddActivityListener(listener);

        var launcher = new FakeAgentProcessLauncher(autoPlay: true)
        {
            ScriptedAnswerChunks = [("The answer.", TimeSpan.Zero)],
        };
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var turnId = await QueryConversationRecordLifecycleTests.SubmitAsync(client, "c-trace-record", "What decisions exist?");
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, turnId, "completed");

        // The supervision span closes shortly after the terminal event is handled.
        await QueryConversationRecordLifecycleTests.WaitUntilAsync(() => Task.FromResult(
            activities.Any(a => a.OperationName == "hub.query.run_supervision" && GetTag(a, "turn_id") == turnId) &&
            activities.Any(a => a.OperationName == "hub.query.record_turn" && GetTag(a, "turn_id") == turnId)));

        var supervision = Assert.Single(activities.Where(a => a.OperationName == "hub.query.run_supervision" && GetTag(a, "turn_id") == turnId));
        var record = Assert.Single(activities.Where(a => a.OperationName == "hub.query.record_turn" && GetTag(a, "turn_id") == turnId));

        Assert.Equal(supervision.SpanId.ToHexString(), record.ParentSpanId.ToHexString());
        Assert.Equal("c-trace-record", GetTag(record, "conversation_id"));
        Assert.Equal(turnId, GetTag(record, "turn_id"));
        Assert.Equal("completed", GetTag(record, "outcome"));

        // Correlation: the load span of the same submission shares the conversation_id.
        var load = Assert.Single(activities.Where(a =>
            a.OperationName == "hub.query.load_conversation_context" && GetTag(a, "conversation_id") == "c-trace-record"));
        Assert.Equal(GetTag(load, "conversation_id"), GetTag(record, "conversation_id"));
    }

    [Fact]
    public async Task RecordTurnSpan_OnUserInterruption_IsParentedByTheInterruptHttpRequest_NotRunSupervision()
    {
        var activities = new ConcurrentQueue<Activity>();
        // The interrupt HTTP request root comes from ASP.NET Core's own source.
        using var listener = CreateListener(activities, "Grimoire.Hub", "Microsoft.AspNetCore");
        ActivitySource.AddActivityListener(listener);

        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var turnId = await QueryConversationRecordLifecycleTests.SubmitAsync(client, "c-trace-interrupt", "Interrupt me?");
        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("answer_chunk", turnId, new { text = "Partial " });
        await QueryConversationRecordLifecycleTests.WaitForAnswerAsync(client, turnId);

        (await client.PostAsync($"/api/query-turns/{turnId}/interrupt", content: null)).EnsureSuccessStatusCode();
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, turnId, "interrupted");

        await QueryConversationRecordLifecycleTests.WaitUntilAsync(() => Task.FromResult(
            activities.Any(a => a.OperationName == "hub.query.record_turn" && GetTag(a, "turn_id") == turnId)));

        var record = Assert.Single(activities.Where(a => a.OperationName == "hub.query.record_turn" && GetTag(a, "turn_id") == turnId));
        Assert.Equal("interrupted", GetTag(record, "outcome"));
        Assert.Equal("c-trace-interrupt", GetTag(record, "conversation_id"));

        // Not parented by run_supervision (which belongs to the submit-time trace) —
        // the interrupt HTTP request is a separate trace root.
        var supervision = activities.SingleOrDefault(a => a.OperationName == "hub.query.run_supervision" && GetTag(a, "turn_id") == turnId);
        if (supervision is not null)
        {
            Assert.NotEqual(supervision.SpanId.ToHexString(), record.ParentSpanId.ToHexString());
        }

        // The direct parent is the interrupt HTTP request's root activity: same trace,
        // and the record span's ParentSpanId points at it. (The submit POST and the
        // status-poll GETs live in their own traces.)
        await QueryConversationRecordLifecycleTests.WaitUntilAsync(() => Task.FromResult(
            activities.Any(a => a.OperationName == "Microsoft.AspNetCore.Hosting.HttpRequestIn" &&
                                a.TraceId == record.TraceId &&
                                a.SpanId.ToHexString() == record.ParentSpanId.ToHexString())));
    }

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;
}
