namespace Grimoire.Hub.OperationalState;

/// <summary>
/// One persisted Remediation Action Task row (015-lint-board-parity data-model.md,
/// ADR-018/ADR-003), sibling of <see cref="QueuedIngestRun"/> in the operational-state
/// store. <see cref="Title"/>/<see cref="Description"/>/<see cref="TargetPath"/> are
/// agent-authored verbatim — the harness never edits them (Principle V);
/// <see cref="AuthorizedAt"/> defines FIFO execution order (FR-017) and is cleared on
/// withdrawal; <see cref="OutcomeReason"/> is mandatory for the <c>failed</c> and
/// <c>not_applicable</c> terminal states (FR-005/FR-018/SC-007).
/// </summary>
public sealed record RemediationTaskRow(
    string TaskId,
    string RunId,
    string Title,
    string Description,
    string? TargetPath,
    string State,
    DateTimeOffset ProposedAt,
    DateTimeOffset? AuthorizedAt,
    string? OutcomeReason,
    DateTimeOffset UpdatedAt);
