using System.Diagnostics.Metrics;
using Grimoire.Hub;
using Grimoire.Hub.QueryConversations;
using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T024/T034/T036 (011-query-conversations, mirrors QueryLifecycleMetricsTests.cs) —
/// business metric emission for every <c>query.conversation.*</c> row in plan.md
/// ## Observability > Business Metrics, validated both at the HubMetrics surface and
/// through the store's real trigger points (append, cached/empty/record loads,
/// fail-closed load).
/// </summary>
public class QueryConversationMetricsTests
{
    private static MeterListener ListenTo<T>(string instrumentName, List<(long Value, KeyValuePair<string, object?>[] Tags)> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == instrumentName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            lock (measurements)
            {
                measurements.Add((value, tags.ToArray()));
            }
        });
        listener.Start();
        return listener;
    }

    private static string? TagValue((long Value, KeyValuePair<string, object?>[] Tags) m, string key)
        => m.Tags.FirstOrDefault(t => t.Key == key).Value?.ToString();

    private static QueryRecordedTurn MakeTurn(int position, string state) => new(
        TurnId: $"t-metrics-{position}",
        Position: position,
        State: state,
        FailureReason: state == "failed" ? "scripted" : null,
        StartedAt: DateTimeOffset.UtcNow,
        CompletedAt: DateTimeOffset.UtcNow,
        Model: null,
        TurnsUsed: null,
        InstructionFilePath: null,
        InstructionFileSha256: null,
        PolicyPath: null,
        PolicyVersion: null,
        PolicySha256: null,
        DeniedActions: [],
        Prompt: $"P{position}?",
        Answer: $"A{position}.");

    private static (QueryConversationRecordStore Store, ResolvedGrimoirePaths Paths) CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-conv-metrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        return (new QueryConversationRecordStore(paths), paths);
    }

    [Fact]
    public async Task TurnsRecordedTotal_IncrementsWithOutcomeLabel_OnEveryAppend()
    {
        var measurements = new List<(long, KeyValuePair<string, object?>[])>();
        using var listener = ListenTo<long>("query.conversation.turns_recorded_total", measurements);

        var (store, _) = CreateStore();
        await store.AppendTurnAsync("c-m1", MakeTurn(1, "completed"));
        await store.AppendTurnAsync("c-m1", MakeTurn(2, "interrupted"));
        await store.AppendTurnAsync("c-m1", MakeTurn(3, "failed"));

        lock (measurements)
        {
            Assert.Contains(measurements, m => m.Item1 == 1L && TagValue(m, "outcome") == "completed");
            Assert.Contains(measurements, m => m.Item1 == 1L && TagValue(m, "outcome") == "interrupted");
            Assert.Contains(measurements, m => m.Item1 == 1L && TagValue(m, "outcome") == "failed");
        }
    }

    [Fact]
    public void RecordAppendFailuresTotal_Increments_OnAppendFailure()
    {
        var measurements = new List<(long, KeyValuePair<string, object?>[])>();
        using var listener = ListenTo<long>("query.conversation.record_append_failures_total", measurements);

        HubMetrics.RecordQueryConversationRecordAppendFailure();

        lock (measurements)
        {
            Assert.Contains(measurements, m => m.Item1 == 1L);
        }
    }

    [Fact]
    public async Task ContextLoadsTotal_ReportsEmptyForNewConversation_AndMemoryForCachedLoads()
    {
        var measurements = new List<(long, KeyValuePair<string, object?>[])>();
        using var listener = ListenTo<long>("query.conversation.context_loads_total", measurements);

        var (store, _) = CreateStore();

        // New conversation: no record file yet.
        var empty = Assert.IsType<QueryConversationContextResult.Loaded>(await store.LoadContextAsync("c-m2"));
        Assert.Equal("empty", empty.Source);

        // After an append the cache serves the load.
        await store.AppendTurnAsync("c-m2", MakeTurn(1, "completed"));
        var memory = Assert.IsType<QueryConversationContextResult.Loaded>(await store.LoadContextAsync("c-m2"));
        Assert.Equal("memory", memory.Source);

        lock (measurements)
        {
            Assert.Contains(measurements, m => m.Item1 == 1L && TagValue(m, "source") == "empty");
            Assert.Contains(measurements, m => m.Item1 == 1L && TagValue(m, "source") == "memory");
        }
    }

    [Fact]
    public async Task ContextLoadsTotal_ReportsRecord_WhenHydratingFromDiskAfterRestart()
    {
        var measurements = new List<(long, KeyValuePair<string, object?>[])>();
        using var listener = ListenTo<long>("query.conversation.context_loads_total", measurements);

        var (store, paths) = CreateStore();
        await store.AppendTurnAsync("c-m3", MakeTurn(1, "completed"));

        // Restart: fresh store over the same paths — cold cache, hydrates from file.
        var restarted = new QueryConversationRecordStore(paths);
        var loaded = Assert.IsType<QueryConversationContextResult.Loaded>(await restarted.LoadContextAsync("c-m3"));
        Assert.Equal("record", loaded.Source);

        lock (measurements)
        {
            Assert.Contains(measurements, m => m.Item1 == 1L && TagValue(m, "source") == "record");
        }
    }

    [Fact]
    public async Task RecordLoadFailuresTotal_Increments_OnFailClosedUnreadableLoad()
    {
        var measurements = new List<(long, KeyValuePair<string, object?>[])>();
        using var listener = ListenTo<long>("query.conversation.record_load_failures_total", measurements);

        var (store, paths) = CreateStore();
        Directory.CreateDirectory(paths.ConversationsDir);
        await File.WriteAllTextAsync(paths.ConversationRecordPathFor("c-m4"), "not a record at all");

        Assert.IsType<QueryConversationContextResult.Unreadable>(await store.LoadContextAsync("c-m4"));

        lock (measurements)
        {
            Assert.Contains(measurements, m => m.Item1 == 1L);
        }
    }
}
