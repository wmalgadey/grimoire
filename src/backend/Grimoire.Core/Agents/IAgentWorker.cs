namespace Grimoire.Core.Agents;

/// <summary>
/// Unified abstraction for all agent worker implementations (ADR-002).
/// Agents are passive: they implement this interface and react to work assignments
/// dispatched by the hub orchestrator. No agent initiates work or communicates
/// with other agents directly (ADR-006 hub-spoke pattern).
/// </summary>
public interface IAgentWorker
{
    /// <summary>
    /// Unique identifier for this agent type (e.g., "ingest", "query", "lint", "batch").
    /// Lowercase, hyphen-separated.
    /// </summary>
    string AgentId { get; }

    /// <summary>Process an input payload and return a result payload.</summary>
    Task<string> ExecuteAsync(string input, CancellationToken cancellationToken = default);

    /// <summary>Signal the agent to stop any in-progress work gracefully. Idempotent.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
