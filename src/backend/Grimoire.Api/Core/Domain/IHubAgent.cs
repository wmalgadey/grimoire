namespace Grimoire.Api.Core.Domain;

/// <summary>
/// Interface for agents managed by the Hub orchestrator.
/// Agents implement this interface for lifecycle management and health checks.
/// Different from Grimoire.Core.Agents.IAgentWorker (work execution contract).
/// </summary>
public interface IHubAgent
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
