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
}
