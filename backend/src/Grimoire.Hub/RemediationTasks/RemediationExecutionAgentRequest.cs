namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// One remediation-execution agent process's spawn request (015-lint-board-parity
/// data-model.md "Execution and message turns", ADR-018). T032 plumbing: carries task
/// identity plus the agent-authored proposal fields verbatim (Principle V — the harness
/// never edits them) and the same wiki/policy/write-lock composition points the Lint-run
/// mode already receives (<see cref="Grimoire.Hub.LintDispatch.LintAgentRequest"/>).
/// T035 extends the shape with <see cref="AttachedContext"/>, the ADR-007-precedented
/// human-attached-context field — carried through to the spawned process
/// (<c>AgentProcessHost.StartRemediationProcess</c>'s <c>--attached-context</c>
/// argument) but nothing populates it yet: US5/T041 adds the attach-context endpoint
/// that will. Until then it stays null and the argument is omitted entirely.
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
    string WriteLocksDir,
    string? AttachedContext = null);
