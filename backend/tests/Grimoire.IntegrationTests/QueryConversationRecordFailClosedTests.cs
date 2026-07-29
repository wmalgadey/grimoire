using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Hub.QueryConversations;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T035/T036 (US3, FR-006) — a structurally unreadable record fails the follow-up
/// submission closed: 500 <c>conversation_record_unreadable</c>, no turn created, no
/// agent spawned, <c>query.conversation.record_load_failed</c> at ERROR and
/// <c>record_load_failures_total</c> incremented; a <b>trailing</b> incomplete block
/// alone is NOT unreadable (context = complete turns, WARN diagnostic); a new
/// conversation afterwards works normally.
/// </summary>
public class QueryConversationRecordFailClosedTests
{
    private static async Task SeedRecordAsync(string root, string conversationId, int turnCount = 2)
    {
        var store = new ConversationRecordStore(QueryTurnSubmissionApiTests.BuildResolvedPaths(root));
        for (var position = 1; position <= turnCount; position++)
        {
            await store.AppendTurnAsync(conversationId, new RecordedTurn(
                TurnId: $"t-seed-{position}",
                Position: position,
                State: "completed",
                FailureReason: null,
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: DateTimeOffset.UtcNow,
                Model: "claude-sonnet-4-5",
                TurnsUsed: 2,
                InstructionFilePath: "agents/query/system-prompt.md",
                InstructionFileSha256: "sha-1",
                PolicyPath: "agents/query/policy.json",
                PolicyVersion: 1,
                PolicySha256: "sha-2",
                DeniedActions: [],
                Prompt: $"Seed prompt {position}?",
                Answer: $"Seed answer {position}."));
        }
    }

    [Theory]
    [InlineData("truncated_frontmatter")]
    [InlineData("malformed_bookkeeping_yaml")]
    [InlineData("body_shorter_than_declared")]
    public async Task CorruptRecord_FailsSubmissionClosed_With500_NoTurn_NoAgent_AndFailureSignals(string corruption)
    {
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        await SeedRecordAsync(root, "c-corrupt");
        var recordPath = paths.ConversationRecordPathFor("c-corrupt");
        var content = await File.ReadAllTextAsync(recordPath);

        var corrupted = corruption switch
        {
            "truncated_frontmatter" => content[..content.IndexOf("record_format", StringComparison.Ordinal)],
            "malformed_bookkeeping_yaml" => content.Replace("position: 1", "position: not-a-number", StringComparison.Ordinal),
            "body_shorter_than_declared" => content.Replace("answer_chars: 14", "answer_chars: 9000", StringComparison.Ordinal),
            _ => throw new InvalidOperationException(corruption),
        };
        Assert.NotEqual(content, corrupted);
        await File.WriteAllTextAsync(recordPath, corrupted);

        var failureMeasurements = new List<long>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "query.conversation.record_load_failures_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            lock (failureMeasurements)
            {
                failureMeasurements.Add(value);
            }
        });
        meterListener.Start();

        var storeLogger = new CaptureLogger<ConversationRecordStore>();
        var store = new ConversationRecordStore(paths, logger: storeLogger);
        var launcher = new FakeAgentProcessLauncher(autoPlay: true, simulatedRunDuration: TimeSpan.FromSeconds(5));
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root, recordStore: store);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/query-conversations/c-corrupt/turns", new { prompt = "A follow-up?" });

        // FR-006 fail-closed: 500 with the machine reason, no turn, no agent process.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("conversation_record_unreadable", body.GetProperty("reason").GetString());
        Assert.Empty(launcher.QueryRequests);

        // T036: the ERROR log event with its mandatory fields…
        var loadFailed = Assert.Single(storeLogger.Entries.Where(e => e.EventName == "query.conversation.record_load_failed"));
        Assert.Equal(LogLevel.Error, loadFailed.Level);
        Assert.Equal("c-corrupt", loadFailed.Fields["conversation_id"]?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(loadFailed.Fields["reason"]?.ToString()));

        // …and the failure counter.
        lock (failureMeasurements)
        {
            Assert.Contains(failureMeasurements, v => v == 1L);
        }

        // Starting a new conversation afterwards works normally.
        var fresh = await client.PostAsJsonAsync(
            "/api/query-conversations/c-fresh-after-corrupt/turns", new { prompt = "New conversation?" });
        Assert.Equal(HttpStatusCode.Accepted, fresh.StatusCode);
        Assert.Single(launcher.QueryRequests);
    }

    [Fact]
    public async Task TrailingIncompleteBlockAlone_IsNotUnreadable_SubmissionSucceedsWithCompleteTurns_AndWarnDiagnostic()
    {
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        await SeedRecordAsync(root, "c-trailing");
        var recordPath = paths.ConversationRecordPathFor("c-trailing");

        // Crash mid-append: a third block's opening sentinel + partial bookkeeping,
        // with no closing '-->' before EOF (contract Parsing rule 4 vs. 5).
        await File.AppendAllTextAsync(recordPath,
            "<!-- grimoire:turn\nturn_id: t-crashed\nposition: 3\nstate: completed\n");

        var storeLogger = new CaptureLogger<ConversationRecordStore>();
        var store = new ConversationRecordStore(paths, logger: storeLogger);
        var launcher = new FakeAgentProcessLauncher(autoPlay: true, simulatedRunDuration: TimeSpan.FromSeconds(5));
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root, recordStore: store);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/query-conversations/c-trailing/turns", new { prompt = "Still works?" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Context = exactly the complete recorded turns; the fragment is dropped.
        Assert.Equal(3, json.GetProperty("position").GetInt32());
        var request = Assert.Single(launcher.QueryRequests);
        Assert.Equal(2, request.PriorTurns.Count);
        Assert.Equal("Seed answer 2.", request.PriorTurns[1].Answer);

        // WARN diagnostic for the dropped fragment.
        var warn = Assert.Single(storeLogger.Entries.Where(e => e.EventName == "query.conversation.trailing_fragment_dropped"));
        Assert.Equal(LogLevel.Warning, warn.Level);
        Assert.Equal("c-trailing", warn.Fields["conversation_id"]?.ToString());
    }
}
