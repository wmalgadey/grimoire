using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.RemediationTasks;
namespace Grimoire.Hub.AgentDispatch;

/// <summary>
/// A started agent child process as seen by the run coordinator: a stream of stdout
/// lines (the NDJSON event channel) and a termination lever. Run outcome is never
/// derived from the exit code (ADR-008).
/// </summary>
public interface IAgentProcessHandle : IAsyncDisposable
{
    IAsyncEnumerable<string> ReadStdoutLinesAsync(CancellationToken cancellationToken);

    /// <summary>Forcefully terminates the agent process tree (liveness failure cleanup).</summary>
    void Terminate();
}

/// <summary>
/// Seam between the Hub orchestration and the real child process (ADR-010 P1), so
/// supervision, queue behavior, and the manual CLI run-to-exit path are hermetically
/// testable with scripted event streams (Principle II).
/// </summary>
public interface IAgentProcessLauncher
{
    Task<IAgentProcessHandle> StartAsync(IngestAgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manual CLI path (`submit-source`): runs the agent to completion and returns the
    /// exit code. Per ADR-008 the exit code remains valid for manual CLI invocation and
    /// diagnostics; the web dispatch path never uses this method.
    /// </summary>
    Task<int> RunToExitAsync(IngestAgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ADR-011: spawns a Query agent process. Port ownership is unchanged from Ingest's
    /// overload above — same interface, same <see cref="IAgentProcessHandle"/> contract —
    /// only the request shape differs (Query has no manual CLI run-to-exit path, so no
    /// analogous <c>RunToExitAsync</c> overload exists).
    /// </summary>
    Task<IAgentProcessHandle> StartAsync(QueryAgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ADR-016 (013-lint-agent): spawns a Lint agent process. Port ownership is unchanged
    /// again — same interface, same <see cref="IAgentProcessHandle"/> contract — only the
    /// request shape differs (Lint has no per-run stdin payload at all, unlike Query's
    /// conversation input).
    /// </summary>
    Task<IAgentProcessHandle> StartAsync(LintAgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ADR-018 (015-lint-board-parity): spawns a remediation-execution agent process.
    /// Port ownership unchanged — same interface, same <see cref="IAgentProcessHandle"/>
    /// contract, only the request shape differs. The <b>only</b> call site permitted to
    /// invoke this overload is
    /// <c>Grimoire.Hub.RemediationTasks.RemediationRunCoordinator.TryStartNextAsync</c>
    /// (enforced by <c>Grimoire.ArchTests.RemediationExecutionDispatchRuleTests</c>,
    /// SC-005/FR-008): the coordinator CAS's the task row <c>Authorized → Executing</c>
    /// under its slot lock <em>before</em> calling this method — execution dispatch is a
    /// structural precondition, never a runtime check.
    /// </summary>
    Task<IAgentProcessHandle> StartAsync(RemediationExecutionAgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ADR-018 (015-lint-board-parity T042): spawns a Lint agent process in its bounded,
    /// read-only message-turn mode (FR-012, Query-turn shape per ADR-011). Port ownership
    /// unchanged — same interface, same <see cref="IAgentProcessHandle"/> contract, only
    /// the request shape differs. Unlike the <see cref="RemediationExecutionAgentRequest"/>
    /// overload above, this call site is <b>not</b> gated by task authorization: a message
    /// turn is advisory Q&amp;A about a <c>Proposed</c> task, never a wiki write, and never
    /// transitions the task's execution state machine — so it carries none of SC-005's
    /// risk. The <b>only</b> call site permitted to invoke this overload is
    /// <c>Grimoire.Hub.RemediationTasks.RemediationMessageTurnCoordinator</c> (enforced by
    /// <c>Grimoire.ArchTests.RemediationExecutionDispatchRuleTests</c>, extended alongside
    /// <c>RemediationRunCoordinator</c> in its allow-list) — a distinct, independently
    /// allow-listed spawn path from execution dispatch, not a weakening of it.
    /// </summary>
    Task<IAgentProcessHandle> StartAsync(RemediationMessageTurnAgentRequest request, CancellationToken cancellationToken = default);
}
