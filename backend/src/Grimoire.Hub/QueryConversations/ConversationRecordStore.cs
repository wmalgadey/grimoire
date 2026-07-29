using System.Collections.Concurrent;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Hub.QueryConversations;

/// <summary>Result of loading a conversation's prior-turn context (research.md R5, FR-006).</summary>
public abstract record ConversationContextResult
{
    /// <summary><c>Source</c> is <c>memory</c> (cache hit), <c>record</c> (hydrated from file), or <c>empty</c> (new conversation).</summary>
    public sealed record Loaded(IReadOnlyList<QueryPriorTurn> Turns, string Source) : ConversationContextResult;

    /// <summary>The record file exists but is structurally unreadable — fail closed, never partial context (FR-006).</summary>
    public sealed record Unreadable(string Reason) : ConversationContextResult;
}

/// <summary>
/// Owns both directions of the Conversation Record (ADR-014): appending terminal turns
/// (append-only, earlier bytes never modified — FR-003) and loading a conversation's
/// recorded turns as follow-up context. Concrete class, directly injected — persistence
/// exemption (Constitution I / ADR-010); confined to <c>Grimoire.Hub.QueryConversations</c>.
/// Appends are naturally serialized per conversation by the one-active-turn guard; the
/// per-conversation lock here is defense in depth (research.md R3).
/// </summary>
public sealed class ConversationRecordStore
{
    private readonly ResolvedGrimoirePaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConversationRecordStore> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _conversationLocks = new();

    // Copy-on-write per-conversation context cache: values are replaced wholesale on
    // append, so cache hits can read without taking the conversation lock.
    private readonly ConcurrentDictionary<string, IReadOnlyList<QueryPriorTurn>> _contextCache = new();

    public ConversationRecordStore(
        ResolvedGrimoirePaths paths,
        TimeProvider? timeProvider = null,
        ILogger<ConversationRecordStore>? logger = null)
    {
        _paths = paths;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ConversationRecordStore>.Instance;
    }

    /// <summary>
    /// Appends one terminal turn: creates the record (frontmatter + first block) in a
    /// single write on the conversation's first terminal turn, appends one complete
    /// block per later terminal turn with a single append-mode write. Never modifies
    /// recorded bytes (FR-003). Maintains the in-memory context cache on each append.
    /// </summary>
    public async Task AppendTurnAsync(string conversationId, RecordedTurn turn, CancellationToken cancellationToken = default)
    {
        var gate = _conversationLocks.GetOrAdd(conversationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = _paths.ConversationRecordPathFor(conversationId);
            var cacheUsable = true;

            if (!File.Exists(path))
            {
                Directory.CreateDirectory(_paths.ConversationsDir);
                var content = ConversationRecordFormat.BuildRecordHeader(conversationId, _timeProvider.GetUtcNow())
                              + ConversationRecordFormat.BuildTurnBlock(turn);
                await File.WriteAllTextAsync(path, content, ConversationRecordFormat.Encoding, cancellationToken);
                _contextCache[conversationId] = [];
                ConversationRecordLogEvents.LogRecordCreated(_logger, conversationId, path);
            }
            else
            {
                // Hub restarted since this conversation began: hydrate the cache from the
                // file before appending, so it stays a complete view. If hydration fails
                // the cache entry stays absent — later context loads then re-parse the
                // file and fail closed as the contract requires.
                if (!_contextCache.ContainsKey(conversationId))
                {
                    cacheUsable = await TryHydrateLockedAsync(conversationId, path, cancellationToken);
                }

                await File.AppendAllTextAsync(
                    path, ConversationRecordFormat.BuildTurnBlock(turn), ConversationRecordFormat.Encoding, cancellationToken);
            }

            if (cacheUsable && _contextCache.TryGetValue(conversationId, out var existing))
            {
                _contextCache[conversationId] = [.. existing, turn.ToPriorTurn()];
            }

            ConversationRecordLogEvents.LogTurnRecorded(_logger, conversationId, turn.TurnId, turn.Position, turn.State);
            HubMetrics.RecordConversationTurnRecorded(turn.State);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Loads the conversation's prior-turn context: from the in-memory cache when
    /// available, by parsing the record file on a cache miss (Hub restart), empty for a
    /// missing file (new conversation). A structurally unreadable record yields a
    /// fail-closed <see cref="ConversationContextResult.Unreadable"/> — never partial
    /// context (research.md R5, FR-006).
    /// </summary>
    public async Task<ConversationContextResult> LoadContextAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        if (_contextCache.TryGetValue(conversationId, out var cached))
        {
            ConversationRecordLogEvents.LogContextLoaded(_logger, conversationId, cached.Count, "memory");
            HubMetrics.RecordConversationContextLoad("memory");
            return new ConversationContextResult.Loaded(cached, "memory");
        }

        var gate = _conversationLocks.GetOrAdd(conversationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_contextCache.TryGetValue(conversationId, out cached))
            {
                ConversationRecordLogEvents.LogContextLoaded(_logger, conversationId, cached.Count, "memory");
                HubMetrics.RecordConversationContextLoad("memory");
                return new ConversationContextResult.Loaded(cached, "memory");
            }

            var path = _paths.ConversationRecordPathFor(conversationId);
            if (!File.Exists(path))
            {
                ConversationRecordLogEvents.LogContextLoaded(_logger, conversationId, 0, "empty");
                HubMetrics.RecordConversationContextLoad("empty");
                return new ConversationContextResult.Loaded([], "empty");
            }

            var content = await File.ReadAllTextAsync(path, ConversationRecordFormat.Encoding, cancellationToken);
            switch (ConversationRecordFormat.Parse(content))
            {
                case ConversationRecordParseResult.Parsed parsed:
                    {
                        if (parsed.DroppedTrailingFragment)
                        {
                            ConversationRecordLogEvents.LogTrailingFragmentDropped(_logger, conversationId);
                        }

                        IReadOnlyList<QueryPriorTurn> turns = [.. parsed.Turns.Select(t => t.ToPriorTurn())];
                        _contextCache[conversationId] = turns;
                        ConversationRecordLogEvents.LogContextLoaded(_logger, conversationId, turns.Count, "record");
                        HubMetrics.RecordConversationContextLoad("record");
                        return new ConversationContextResult.Loaded(turns, "record");
                    }

                case ConversationRecordParseResult.Unreadable unreadable:
                    ConversationRecordLogEvents.LogRecordLoadFailed(_logger, conversationId, unreadable.Reason);
                    HubMetrics.RecordConversationRecordLoadFailure();
                    return new ConversationContextResult.Unreadable(unreadable.Reason);

                default:
                    throw new InvalidOperationException("Unknown parse result type.");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Cache hydration under the conversation lock (append path); false when the file is unreadable.</summary>
    private async Task<bool> TryHydrateLockedAsync(string conversationId, string path, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, ConversationRecordFormat.Encoding, cancellationToken);
        if (ConversationRecordFormat.Parse(content) is not ConversationRecordParseResult.Parsed parsed)
        {
            return false;
        }

        if (parsed.DroppedTrailingFragment)
        {
            ConversationRecordLogEvents.LogTrailingFragmentDropped(_logger, conversationId);
        }

        _contextCache[conversationId] = [.. parsed.Turns.Select(t => t.ToPriorTurn())];
        return true;
    }
}
