namespace Grimoire.Hub.AgentDispatch;

/// <summary>
/// Enforces the project's existing single-concurrent-ingest-run constraint (ADR-002, FR-013) for
/// runs triggered by the ingest-submission pipeline: at most one Ingest agent child process runs
/// at a time. A task reaching `queued` while another run is in progress waits here and proceeds
/// automatically the moment the prior run releases the gate — no user action required.
/// </summary>
public sealed class IngestRunGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
