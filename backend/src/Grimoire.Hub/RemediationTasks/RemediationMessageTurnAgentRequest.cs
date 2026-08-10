namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// One side of a human⇄agent exchange already recorded in the task's Remediation Task
/// Record, as supplied to a new message-turn spawn (015-lint-board-parity data-model.md
/// "Task Message", ADR-018 R6 — record-as-context). Mirrors
/// <c>Grimoire.Hub.QueryDispatch.QueryPriorTurn</c>'s role: the record is the single
/// source, so what the human sees in the thread and what the agent receives as context
/// can never diverge.
/// </summary>
public sealed record RemediationPriorMessage(string Sender, string Text);

/// <summary>
/// One message-turn agent process's spawn request (015-lint-board-parity T042, ADR-018
/// "Message-turn mode": a bounded, read-only single exchange reusing the Query-turn shape,
/// ADR-011). Carries the same proposal-identity fields as
/// <see cref="RemediationExecutionAgentRequest"/> (task is harness-opaque, agent-authored
/// text verbatim — Principle V) plus this turn's new human message and every prior
/// message already in the task's Remediation Task Record (built Hub-side from
/// <c>RemediationTaskRecordStore.ReadAsync</c> before the human message being sent right
/// now is appended, so "prior" means exactly what the record already held).
/// </summary>
public sealed record RemediationMessageTurnAgentRequest(
    string TaskId,
    string RunId,
    string Title,
    string Description,
    string? TargetPath,
    string WikiRoot,
    string SystemPromptPath,
    string PolicyPath,
    string WriteLocksDir,
    string? AttachedContext,
    string Message,
    IReadOnlyList<RemediationPriorMessage> PriorMessages,
    // ADR-023 (022-align-wiki-structure, Phase 5): the effective granted-surface list for
    // this run (Grimoire:HarnessSurfaceReads), threaded to AgentProcessHost's
    // --granted-harness-surfaces spawn argument. Empty means none granted (deny-by-default).
    IReadOnlyList<string>? GrantedHarnessSurfaces = null);
