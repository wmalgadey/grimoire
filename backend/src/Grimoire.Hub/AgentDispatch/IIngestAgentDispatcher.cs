namespace Grimoire.Hub.AgentDispatch;

/// <summary>
/// Abstraction over <see cref="IngestAgentDispatcher"/> so the ingest-submission pipeline's
/// auto-trigger (FR-010) can be tested with a fake dispatcher (Test Strategy, SC-006) instead of
/// spawning a real child process that would need live model credentials.
/// </summary>
public interface IIngestAgentDispatcher
{
    Task<int> DispatchAsync(IngestAgentRequest request, CancellationToken cancellationToken = default);
}
