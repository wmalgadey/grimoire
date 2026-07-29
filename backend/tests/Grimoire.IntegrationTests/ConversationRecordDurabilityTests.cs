using System.Diagnostics.Metrics;
using Grimoire.Hub.QueryConversations;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T033/T034 (US3, SC-003/SC-005 from-file path) — the record on disk survives a Hub
/// restart (re-instantiated host + store over the same base dir) with finished turns
/// byte-complete and a mid-stream-killed turn recorded with its accumulated partial
/// answer and the supervision-consistent terminal state; a follow-up after the restart
/// hydrates its context from the record file (source=record) tuple-equal to the parsed
/// record.
/// </summary>
public class ConversationRecordDurabilityTests
{
    [Fact]
    public async Task Restart_RecordContainsFinishedTurnsByteComplete_AndKilledTurnWithPartialAnswer()
    {
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = paths.ConversationRecordPathFor("c-durable");

        byte[] bytesBeforeRestart;
        {
            var launcher = new FakeAgentProcessLauncher(autoPlay: false);
            using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(
                launcher, root, livenessWindow: TimeSpan.FromMilliseconds(200));
            var client = host.GetTestClient();

            await ConversationRecordLifecycleTests.RunScriptedTurnAsync(client, launcher, 0, "c-durable",
                prompt: "First?", answerChunks: ["First answer."], terminalExtra: new { summary = "done" });
            await ConversationRecordLifecycleTests.RunScriptedTurnAsync(client, launcher, 1, "c-durable",
                prompt: "Second?", answerChunks: ["Second answer."], terminalExtra: new { summary = "done" });

            // Third turn: the agent is killed mid-stream (chunk, then silence — the
            // liveness watchdog terminates it per the existing supervision rules).
            var killedTurnId = await ConversationRecordLifecycleTests.SubmitAsync(client, "c-durable", "Third, killed midway?");
            launcher.Handles[2].EmitEvent("started", killedTurnId);
            launcher.Handles[2].EmitEvent("answer_chunk", killedTurnId, new { text = "Partial third " });
            await ConversationRecordLifecycleTests.WaitForAnswerAsync(client, killedTurnId);
            await ConversationRecordLifecycleTests.WaitForStateAsync(client, killedTurnId, "failed");

            await ConversationRecordLifecycleTests.WaitUntilAsync(() => Task.FromResult(
                File.Exists(recordPath) &&
                ConversationRecordFormat.Parse(File.ReadAllText(recordPath)) is ConversationRecordParseResult.Parsed { Turns.Count: 3 }));

            bytesBeforeRestart = await File.ReadAllBytesAsync(recordPath);
        }

        // Hub restart: nothing in memory survives; only the file remains.
        var bytesAfterRestart = await File.ReadAllBytesAsync(recordPath);
        Assert.Equal(bytesBeforeRestart, bytesAfterRestart);

        var parsed = Assert.IsType<ConversationRecordParseResult.Parsed>(
            ConversationRecordFormat.Parse(await File.ReadAllTextAsync(recordPath)));
        Assert.False(parsed.DroppedTrailingFragment);
        Assert.Equal(3, parsed.Turns.Count);

        Assert.Equal("completed", parsed.Turns[0].State);
        Assert.Equal("First answer.", parsed.Turns[0].Answer);
        Assert.Equal("completed", parsed.Turns[1].State);
        Assert.Equal("Second answer.", parsed.Turns[1].Answer);

        var killed = parsed.Turns[2];
        Assert.Equal("failed", killed.State);
        Assert.Contains("liveness", killed.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Partial third ", killed.Answer);
    }

    [Fact]
    public async Task FollowUpAfterRestart_HydratesContextFromTheRecordFile_TupleEqualAndSourceRecord()
    {
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = paths.ConversationRecordPathFor("c-rehydrate");

        {
            var launcher = new FakeAgentProcessLauncher(autoPlay: false);
            using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
            var client = host.GetTestClient();

            await ConversationRecordLifecycleTests.RunScriptedTurnAsync(client, launcher, 0, "c-rehydrate",
                prompt: "First?", answerChunks: ["First answer."], terminalExtra: new { summary = "done" });

            // Interrupted second turn: its partial answer must reach the hydrated context.
            var turn2 = await ConversationRecordLifecycleTests.SubmitAsync(client, "c-rehydrate", "Second, interrupted?");
            launcher.Handles[1].EmitEvent("started", turn2);
            launcher.Handles[1].EmitEvent("answer_chunk", turn2, new { text = "Partial second " });
            await ConversationRecordLifecycleTests.WaitForAnswerAsync(client, turn2);
            (await client.PostAsync($"/api/query-turns/{turn2}/interrupt", content: null)).EnsureSuccessStatusCode();
            await ConversationRecordLifecycleTests.WaitForStateAsync(client, turn2, "interrupted");

            await ConversationRecordLifecycleTests.WaitUntilAsync(() => Task.FromResult(
                File.Exists(recordPath) &&
                ConversationRecordFormat.Parse(File.ReadAllText(recordPath)) is ConversationRecordParseResult.Parsed { Turns.Count: 2 }));
        }

        // Restart: fresh host + fresh store (cold cache) over the same base dir.
        var contextLoadMeasurements = new List<(long Value, string? Source)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "query.conversation.context_loads_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var source = tags.ToArray().FirstOrDefault(t => t.Key == "source").Value?.ToString();
            lock (contextLoadMeasurements)
            {
                contextLoadMeasurements.Add((value, source));
            }
        });
        meterListener.Start();

        var storeLogger = new CaptureLogger<ConversationRecordStore>();
        var restartedStore = new ConversationRecordStore(
            QueryTurnSubmissionApiTests.BuildResolvedPaths(root), logger: storeLogger);
        var restartedLauncher = new FakeAgentProcessLauncher(autoPlay: true, simulatedRunDuration: TimeSpan.FromSeconds(5));
        using var restartedHost = await QueryTurnSubmissionApiTests.BuildHostAsync(
            restartedLauncher, root, recordStore: restartedStore);
        var restartedClient = restartedHost.GetTestClient();

        await ConversationRecordLifecycleTests.SubmitAsync(restartedClient, "c-rehydrate", "How do those relate?");

        var request = Assert.Single(restartedLauncher.QueryRequests);
        var parsed = Assert.IsType<ConversationRecordParseResult.Parsed>(
            ConversationRecordFormat.Parse(await File.ReadAllTextAsync(recordPath)));
        Assert.Equal(parsed.Turns.Select(t => t.ToPriorTurn()).ToList(), request.PriorTurns);
        Assert.Equal("Partial second ", request.PriorTurns[1].Answer);
        Assert.Equal("interrupted", request.PriorTurns[1].State);

        // SC-005 from-file: the context load reports source=record in the log event…
        var contextLoaded = Assert.Single(storeLogger.Entries.Where(e => e.EventName == "query.conversation.context_loaded"));
        Assert.Equal(LogLevel.Information, contextLoaded.Level);
        Assert.Equal("c-rehydrate", contextLoaded.Fields["conversation_id"]?.ToString());
        Assert.Equal("2", contextLoaded.Fields["turn_count"]?.ToString());
        Assert.Equal("record", contextLoaded.Fields["source"]?.ToString());

        // …and in the metric.
        lock (contextLoadMeasurements)
        {
            Assert.Contains(contextLoadMeasurements, m => m.Value == 1L && m.Source == "record");
        }
    }
}
