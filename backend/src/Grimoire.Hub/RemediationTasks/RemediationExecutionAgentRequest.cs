namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// One remediation-execution agent process's spawn request (015-lint-board-parity
/// data-model.md "Execution and message turns", ADR-018). T032 plumbing: carries task
/// identity plus the agent-authored proposal fields verbatim (Principle V — the harness
/// never edits them) and the same wiki/policy/write-lock composition points the Lint-run
/// mode already receives (<see cref="Grimoire.Hub.LintDispatch.LintAgentRequest"/>). This
/// is deliberately minimal: T035 extends the shape with the ADR-007 attached-context
/// user-prompt override and any execution-mode-specific instruction/policy paths the
/// agent needs for re-verification (FR-018); until then, this carries exactly what
/// <see cref="RemediationRunCoordinator"/> needs to spawn and supervise the process over
/// the unchanged NDJSON event channel (ADR-008).
/// </summary>
public sealed record RemediationExecutionAgentRequest(
    string TaskId,
    string RunId,
    string Title,
    string Description,
    string? TargetPath,
    string WikiRoot,
    string SystemPromptPath,
    string PolicyPath,
    string WriteLocksDir);
