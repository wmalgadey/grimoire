namespace Grimoire.Hub.Realtime;

/// <summary>
/// SignalR hub for Lint Run lifecycle updates on the board (015-lint-board-parity T011,
/// contracts/remediation-lifecycle-events.md "Hub 1: Lint lifecycle"). Route:
/// <c>/hubs/lint-lifecycle</c>. Clients receive events on the <c>lintRunLifecycleChanged</c>
/// channel; broadcast-only (no server-invokable methods), published to via
/// <see cref="LintLifecyclePublisher"/> — sibling to <see cref="IngestLifecycleHub"/> and
/// <see cref="QueryLifecycleHub"/>, structurally independent so lint realtime traffic
/// never couples to ingest's message shapes (FR-015, research.md R1).
/// </summary>
/// <remarks>
/// Inherits the fully-qualified SignalR type: within the <c>Grimoire.Hub</c> root namespace,
/// the unqualified name "Hub" resolves to that enclosing namespace rather than
/// <c>Microsoft.AspNetCore.SignalR.Hub</c>.
/// </remarks>
public sealed class LintLifecycleHub : Microsoft.AspNetCore.SignalR.Hub
{
}
