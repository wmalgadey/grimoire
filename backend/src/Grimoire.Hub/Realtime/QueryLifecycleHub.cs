namespace Grimoire.Hub.Realtime;

/// <summary>
/// SignalR hub for Query Turn realtime updates (contracts/query-conversation-api.md).
/// Route: <c>/hubs/query-lifecycle</c>. Broadcast-only (no server-invokable methods),
/// sibling to <see cref="IngestLifecycleHub"/> — structurally independent so query
/// realtime traffic never couples to ingest's message shapes (FR-017, research.md R8).
/// </summary>
public sealed class QueryLifecycleHub : Microsoft.AspNetCore.SignalR.Hub
{
}
