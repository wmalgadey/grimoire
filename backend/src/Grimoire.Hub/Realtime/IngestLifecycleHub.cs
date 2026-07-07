namespace Grimoire.Hub.Realtime;

/// <summary>
/// SignalR hub for board lifecycle updates (contracts/ingest-lifecycle-events.md). Route:
/// `/hubs/ingest-lifecycle`. Clients receive events on the `taskLifecycleChanged` channel; this
/// hub itself has no server-invokable methods — it is a broadcast-only channel, published to via
/// <see cref="IngestLifecyclePublisher"/>.
/// </summary>
/// <remarks>
/// Inherits the fully-qualified SignalR type: within the <c>Grimoire.Hub</c> root namespace, the
/// unqualified name "Hub" resolves to that enclosing namespace rather than
/// <c>Microsoft.AspNetCore.SignalR.Hub</c>.
/// </remarks>
public sealed class IngestLifecycleHub : Microsoft.AspNetCore.SignalR.Hub
{
}
