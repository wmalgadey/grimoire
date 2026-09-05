namespace Grimoire.LintAgent;

/// <summary>
/// CLI options for one remediation-execution agent process spawn (T035,
/// 015-lint-board-parity, ADR-018). Spawned by
/// <c>Grimoire.Hub.RemediationTasks.RemediationRunCoordinator.TryStartNextAsync</c>
/// exclusively, never manually (unlike Ingest's <c>submit-source</c> path). The proposal
/// fields are the agent-authored text from the Remediation Action Task's materialization
/// (ADR-018 "Proposals ride the Lint terminal event") carried through verbatim
/// (Principle V) — the harness never edits or interprets them.
/// </summary>
public sealed record RemediationExecutionCliOptions(
    string TaskId,
    string RunId,
    string WikiRoot,
    string FoundationPromptPath,
    string SystemPromptPath,
    string PolicyPath,
    string WriteLocksDir,
    string ProposalTitle,
    string ProposalDescription,
    string? ProposalTargetPath,
    // ADR-007 user-prompt-override precedent: human-attached context (US5/T041) rides
    // the kickoff message alongside the proposal, once the Hub starts populating it.
    // Optional and unset today — RemediationExecutionAgentRequest carries the field, but
    // nothing sets it until US5's attach-context endpoint exists.
    string? AttachedContext = null,
    int HeartbeatSeconds = 10);
