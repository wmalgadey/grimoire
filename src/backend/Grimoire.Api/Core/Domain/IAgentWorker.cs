namespace Grimoire.Api.Core.Domain;

/// <summary>
/// Interface that all agent implementations must provide to be managed by the Hub orchestrator.
/// Agents are passive: they implement this interface and react to work dispatched by the Hub.
/// No agent initiates work or communicates with other agents directly (ADR-006 hub-spoke pattern).
/// </summary>
public interface IAgentWorker
{
    /// <summary>
    /// Unique identifier for this agent type (e.g., "ingest", "query", "lint").
    /// Lowercase, hyphen-separated.
    /// </summary>
    string AgentId { get; }

    /// <summary>
    /// Agent descriptor with metadata and current status.
    /// </summary>
    AgentDescriptor Descriptor { get; }

    /// <summary>
    /// Retrieve the current health status of the agent.
    /// Called by the Hub to monitor agent health and detect faults.
    /// </summary>
    Task<AgentHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signal the agent to start operations (transition from Unregistered to Running).
    /// Implementations should prepare resources and become ready to process work.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signal the agent to stop operations gracefully (transition from Running to Stopped).
    /// Implementations should clean up resources and cease processing.
    /// Idempotent: safe to call multiple times.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
