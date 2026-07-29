using Grimoire.Hub.QueryConversations;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T010 (011-query-conversations, Phase 2) — <see cref="ConversationRecordStore"/>
/// against a real temp filesystem: create-on-first-append vs. append-on-later,
/// never-rewrite of earlier bytes (FR-003), strictly increasing positions, cache
/// behavior across appends/misses/restarts, missing-file empty context, fail-closed
/// unreadable result, and per-conversation file isolation under concurrency.
/// </summary>
public class QueryConversationRecordStoreTests
{
    private static (ConversationRecordStore Store, ResolvedGrimoirePaths Paths, string Root) CreateStore(string? existingRoot = null)
    {
        var root = existingRoot ?? Path.Combine(Path.GetTempPath(), $"grimoire-record-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        return (new ConversationRecordStore(paths), paths, root);
    }

    private static RecordedTurn MakeTurn(int position, string state = "completed", string? answer = null) => new(
        TurnId: $"2026-07-29-query-store{position:D2}",
        Position: position,
        State: state,
        FailureReason: state == "failed" ? "scripted failure" : null,
        StartedAt: DateTimeOffset.UtcNow,
        CompletedAt: DateTimeOffset.UtcNow,
        Model: "claude-sonnet-4-5",
        TurnsUsed: 2,
        InstructionFilePath: "agents/query/system-prompt.md",
        InstructionFileSha256: "abc123",
        PolicyPath: "agents/query/policy.json",
        PolicyVersion: 1,
        PolicySha256: "def456",
        DeniedActions: [],
        Prompt: $"Prompt {position}?",
        Answer: answer ?? $"Answer {position}.");

    [Fact]
    public async Task FirstAppend_CreatesTheRecordFile_WithFrontmatterAndFirstBlock()
    {
        var (store, paths, _) = CreateStore();

        await store.AppendTurnAsync("c-create", MakeTurn(1));

        var path = paths.ConversationRecordPathFor("c-create");
        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        Assert.StartsWith("---\n", content, StringComparison.Ordinal);
        Assert.Contains("record_format: grimoire-conversation/1", content, StringComparison.Ordinal);
        Assert.Contains("# Conversation c-create", content, StringComparison.Ordinal);
        Assert.Contains("## Turn 1 — completed", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaterAppend_NeverModifiesEarlierBytes_FR003()
    {
        var (store, paths, _) = CreateStore();
        var path = paths.ConversationRecordPathFor("c-append");

        await store.AppendTurnAsync("c-append", MakeTurn(1));
        var bytesAfterFirst = await File.ReadAllBytesAsync(path);

        await store.AppendTurnAsync("c-append", MakeTurn(2, state: "interrupted"));
        var bytesAfterSecond = await File.ReadAllBytesAsync(path);

        Assert.True(bytesAfterSecond.Length > bytesAfterFirst.Length);
        Assert.Equal(bytesAfterFirst, bytesAfterSecond[..bytesAfterFirst.Length]);
    }

    [Fact]
    public async Task Appends_YieldStrictlyIncreasingPositionsFromOne()
    {
        var (store, paths, _) = CreateStore();
        await store.AppendTurnAsync("c-positions", MakeTurn(1));
        await store.AppendTurnAsync("c-positions", MakeTurn(2));
        await store.AppendTurnAsync("c-positions", MakeTurn(3));

        var content = await File.ReadAllTextAsync(paths.ConversationRecordPathFor("c-positions"));
        var parsed = Assert.IsType<ConversationRecordParseResult.Parsed>(ConversationRecordFormat.Parse(content));
        Assert.Equal([1, 2, 3], parsed.Turns.Select(t => t.Position));
    }

    [Fact]
    public async Task CacheServesLoads_AfterAppends_WithoutRereadingTheFile()
    {
        var (store, paths, _) = CreateStore();
        await store.AppendTurnAsync("c-cache", MakeTurn(1));
        await store.AppendTurnAsync("c-cache", MakeTurn(2));

        // Corrupt the file after the appends: a memory-served load must not notice.
        await File.WriteAllTextAsync(paths.ConversationRecordPathFor("c-cache"), "no longer a record");

        var result = await store.LoadContextAsync("c-cache");

        var loaded = Assert.IsType<ConversationContextResult.Loaded>(result);
        Assert.Equal("memory", loaded.Source);
        Assert.Equal(2, loaded.Turns.Count);
        Assert.Equal(new QueryPriorTurn(1, "Prompt 1?", "Answer 1.", "completed"), loaded.Turns[0]);
        Assert.Equal(new QueryPriorTurn(2, "Prompt 2?", "Answer 2.", "completed"), loaded.Turns[1]);
    }

    [Fact]
    public async Task CacheMiss_HydratesFromDisk_HubRestart()
    {
        var (store, _, root) = CreateStore();
        await store.AppendTurnAsync("c-restart", MakeTurn(1));
        await store.AppendTurnAsync("c-restart", MakeTurn(2, state: "interrupted", answer: "partial ans"));

        // Hub restart: a fresh store over the same base dir has a cold cache.
        var (restartedStore, _, _) = CreateStore(existingRoot: root);
        var result = await restartedStore.LoadContextAsync("c-restart");

        var loaded = Assert.IsType<ConversationContextResult.Loaded>(result);
        Assert.Equal("record", loaded.Source);
        Assert.Equal(2, loaded.Turns.Count);
        Assert.Equal(new QueryPriorTurn(2, "Prompt 2?", "partial ans", "interrupted"), loaded.Turns[1]);

        // The hydrated turns are now cached: a second load serves from memory.
        var second = Assert.IsType<ConversationContextResult.Loaded>(await restartedStore.LoadContextAsync("c-restart"));
        Assert.Equal("memory", second.Source);
    }

    [Fact]
    public async Task MissingFile_YieldsEmptyContext_NewConversation()
    {
        var (store, _, _) = CreateStore();

        var result = await store.LoadContextAsync("c-brand-new");

        var loaded = Assert.IsType<ConversationContextResult.Loaded>(result);
        Assert.Equal("empty", loaded.Source);
        Assert.Empty(loaded.Turns);
    }

    [Fact]
    public async Task UnreadableFile_YieldsFailClosedResult_NeverPartialContext()
    {
        var (store, paths, _) = CreateStore();
        Directory.CreateDirectory(paths.ConversationsDir);
        await File.WriteAllTextAsync(
            paths.ConversationRecordPathFor("c-corrupt"),
            "---\nconversation_id: c-corrupt\n");

        var result = await store.LoadContextAsync("c-corrupt");

        var unreadable = Assert.IsType<ConversationContextResult.Unreadable>(result);
        Assert.False(string.IsNullOrWhiteSpace(unreadable.Reason));
    }

    [Fact]
    public async Task ConcurrentAppends_ToDifferentConversations_LandInTheirOwnFilesOnly()
    {
        var (store, paths, _) = CreateStore();

        var appends = new List<Task>();
        for (var position = 1; position <= 5; position++)
        {
            var turnA = MakeTurn(position) with { Prompt = $"A{position}?", Answer = $"A{position}." };
            var turnB = MakeTurn(position) with { Prompt = $"B{position}?", Answer = $"B{position}." };
            appends.Add(store.AppendTurnAsync("c-concurrent-a", turnA));
            appends.Add(store.AppendTurnAsync("c-concurrent-b", turnB));
        }

        await Task.WhenAll(appends);

        var contentA = await File.ReadAllTextAsync(paths.ConversationRecordPathFor("c-concurrent-a"));
        var contentB = await File.ReadAllTextAsync(paths.ConversationRecordPathFor("c-concurrent-b"));

        var parsedA = Assert.IsType<ConversationRecordParseResult.Parsed>(ConversationRecordFormat.Parse(contentA));
        var parsedB = Assert.IsType<ConversationRecordParseResult.Parsed>(ConversationRecordFormat.Parse(contentB));

        Assert.Equal(5, parsedA.Turns.Count);
        Assert.Equal(5, parsedB.Turns.Count);
        Assert.All(parsedA.Turns, t => Assert.StartsWith("A", t.Prompt, StringComparison.Ordinal));
        Assert.All(parsedB.Turns, t => Assert.StartsWith("B", t.Prompt, StringComparison.Ordinal));
    }
}
