namespace Grimoire.Api.Agents.Models;

/// <summary>
/// Interface for agents managed by the Hub orchestrator (lifecycle + health).
/// Distinct from Grimoire.Core.Agents.IAgentWorker (work execution contract).
/// </summary>
public interface IHubAgent
{
    string AgentId { get; }
    AgentDescriptor Descriptor { get; }
    Task<AgentHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
