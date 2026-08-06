using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Hub.QueryConversations;
using Grimoire.Hub.QueryDispatch;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T022/T036 (011-query-conversations, mirrors QueryLifecycleLogEventTests.cs) —
/// validates event name, level, and every mandatory field for the
/// <c>query.conversation.*</c> structured log events declared in plan.md
/// ## Observability > Structured Log Events, plus the append-failure isolation
/// guarantee: a rigged record-write failure emits
/// <c>query.conversation.record_append_failed</c> while the turn still reaches its
/// terminal state and the <c>queryTurnChanged</c> publish still fires.
/// </summary>
[Collection("HubActivityListenerObservability")]
public class QueryConversationLogEventTests
{
    [Fact]
    public void ConversationRecordStructuredEvents_EmitExpectedNamesLevelsAndFields()
    {
        var logger = new CaptureLogger<QueryConversationLogEventTests>();

        ConversationRecordLogEvents.LogRecordCreated(logger, conversationId: "c-1", path: "/data/conversations/c-1.md");
        ConversationRecordLogEvents.LogTurnRecorded(logger, conversationId: "c-1", turnId: "t-1", position: 1, outcome: "completed");
        ConversationRecordLogEvents.LogContextLoaded(logger, conversationId: "c-1", turnCount: 2, source: "memory");
        ConversationRecordLogEvents.LogRecordAppendFailed(logger, conversationId: "c-1", turnId: "t-2", reason: "disk full");
        ConversationRecordLogEvents.LogRecordLoadFailed(logger, conversationId: "c-1", reason: "malformed bookkeeping");

        AssertEvent(logger.Entries, "query.conversation.record_created", LogLevel.Information, ["conversation_id", "path"]);
        AssertEvent(logger.Entries, "query.conversation.turn_recorded", LogLevel.Information, ["conversation_id", "turn_id", "position", "outcome"]);
        AssertEvent(logger.Entries, "query.conversation.context_loaded", LogLevel.Information, ["conversation_id", "turn_count", "source"]);
        AssertEvent(logger.Entries, "query.conversation.record_append_failed", LogLevel.Error, ["conversation_id", "turn_id", "reason"]);
        AssertEvent(logger.Entries, "query.conversation.record_load_failed", LogLevel.Error, ["conversation_id", "reason"]);
    }

    [Fact]
    public async Task RiggedAppendFailure_EmitsRecordAppendFailed_AndNeverSuppressesTerminalStateOrPublish()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var coordinatorLogger = new CaptureLogger<QueryRunCoordinator>();
        var launcher = new FakeAgentProcessLauncher(autoPlay: true)
        {
            ScriptedAnswerChunks = [("The answer.", TimeSpan.Zero)],
        };
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();

        // Rig the store to throw on append: a directory occupies the record file path,
        // so the file write inside AppendTurnAsync fails.
        Directory.CreateDirectory(Path.Combine(root, "conversations", "c-appendfail.md"));

        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(
            launcher, root, coordinatorLogger: coordinatorLogger);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/query-conversations/c-appendfail/turns", new { prompt = "Will the append fail?" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var turnId = json.GetProperty("turnId").GetString()!;

        // Isolation guarantee (research.md R6): the turn still reaches its terminal state.
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, turnId, "completed");

        // The ERROR event was emitted with all mandatory fields.
        await QueryConversationRecordLifecycleTests.WaitUntilAsync(() => Task.FromResult(
            coordinatorLogger.Entries.Any(e => e.EventName == "query.conversation.record_append_failed")));
        var entry = Assert.Single(coordinatorLogger.Entries.Where(e => e.EventName == "query.conversation.record_append_failed"));
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("c-appendfail", entry.Fields["conversation_id"]?.ToString());
        Assert.Equal(turnId, entry.Fields["turn_id"]?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(entry.Fields["reason"]?.ToString()));

        // ... and the queryTurnChanged publish still fired (stage=completed span).
        await QueryConversationRecordLifecycleTests.WaitUntilAsync(() => Task.FromResult(
            activities.Any(a => a.OperationName == "hub.query_lifecycle.publish_update" &&
                                GetTag(a, "turn_id") == turnId &&
                                GetTag(a, "stage") == "completed")));
    }

    private static void AssertEvent(
        List<CaptureLoggerEntry> entries,
        string eventName,
        LogLevel level,
        string[] requiredFields)
    {
        var entry = Assert.Single(entries.Where(e => e.EventName == eventName));
        Assert.Equal(level, entry.Level);

        foreach (var field in requiredFields)
        {
            Assert.True(entry.Fields.ContainsKey(field), $"Missing field '{field}' for event '{eventName}'.");
        }
    }

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;
}
