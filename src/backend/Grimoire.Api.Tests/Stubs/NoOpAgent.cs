using Grimoire.Api.Core.Domain;

namespace Grimoire.Api.Tests.Stubs;

/// <summary>
/// No-op implementation of IHubAgent for testing the Hub without real agent implementations.
/// Returns healthy status, completes start/stop immediately.
/// </summary>
public class NoOpAgent : IHubAgent
{
    public string AgentId { get; }

    public AgentDescriptor Descriptor
    {
        get
        {
            return new AgentDescriptor
            {
                AgentId = AgentId,
                Name = $"NoOp Agent ({AgentId})",
                Status = AgentStatus.Running,
                Capabilities = Array.Empty<string>(),
                RegisteredAt = DateTime.UtcNow,
                LastHealthCheckAt = DateTime.UtcNow
            };
        }
    }

    public NoOpAgent(string agentId = "noop-agent")
    {
        AgentId = agentId;
    }

    public Task<AgentHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AgentHealthStatus
        {
            AgentId = AgentId,
            IsHealthy = true,
            CheckedAt = DateTime.UtcNow,
            Message = "No-op agent is healthy"
        });
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
