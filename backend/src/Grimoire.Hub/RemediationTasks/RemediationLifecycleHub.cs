namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// SignalR hub for Remediation Action Task lifecycle updates on the board
/// (015-lint-board-parity T023, contracts/remediation-lifecycle-events.md "Hub 2:
/// Remediation lifecycle"). Route: <c>/hubs/remediation-lifecycle</c>. Clients receive
/// events on the <c>remediationTaskLifecycleChanged</c> channel (US4 adds
/// <c>remediationRunActivityChanged</c>, US5 <c>remediationMessageTurnChanged</c>);
/// broadcast-only (no server-invokable methods), published to via
/// <see cref="RemediationLifecyclePublisher"/> — sibling to
/// <c>Grimoire.Hub.Realtime.LintLifecycleHub</c>/<c>IngestLifecycleHub</c>, structurally
/// independent so remediation realtime traffic never couples to ingest's message shapes
/// (FR-015, research.md R1).
/// </summary>
/// <remarks>
/// Inherits the fully-qualified SignalR type: within the <c>Grimoire.Hub</c> root
/// namespace, the unqualified name "Hub" resolves to that enclosing namespace rather
/// than <c>Microsoft.AspNetCore.SignalR.Hub</c>.
/// </remarks>
public sealed class RemediationLifecycleHub : Microsoft.AspNetCore.SignalR.Hub
{
}
