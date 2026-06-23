using System.Diagnostics;
using Grimoire.Api.Api.Hubs;
using Grimoire.Api.Core.Domain;
using Grimoire.Api.Infrastructure.Observability;
using Grimoire.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;

namespace Grimoire.Api.Api.Handlers;

/// <summary>
/// Application-layer handler orchestrating agent registration and lifecycle operations.
/// Coordinates domain logic, persistence, metrics, and real-time SignalR notifications.
/// </summary>
public class HubOrchestrationHandler
{
    private readonly HubAgentRegistry _registry;
    private readonly AgentRepository _repository;
    private readonly HubMetrics _metrics;
    private readonly IHubContext<AgentHub> _hubContext;
    private readonly ILogger<HubOrchestrationHandler> _logger;

    public HubOrchestrationHandler(
        HubAgentRegistry registry,
        AgentRepository repository,
        HubMetrics metrics,
        IHubContext<AgentHub> hubContext,
        ILogger<HubOrchestrationHandler> logger)
    {
        _registry = registry;
        _repository = repository;
        _metrics = metrics;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Registers a new agent, persists it, and emits a registration metric.
    /// </summary>
    public async Task<AgentDescriptor> RegisterAgentAsync(string agentId, string name, string[] capabilities)
    {
        using (var activity = HubTracing.StartRegisterAgent(agentId, name))
        {
            var descriptor = new AgentDescriptor
            {
                AgentId = agentId,
                Name = name,
                Status = AgentStatus.Unregistered,
                Capabilities = capabilities,
                RegisteredAt = DateTime.UtcNow
            };

            _registry.RegisterAgent(descriptor);
            await _repository.SaveAgentDescriptorAsync(descriptor);
            _metrics.AgentRegisteredTotal.Add(1, new KeyValuePair<string, object?>("agent_id", agentId), new KeyValuePair<string, object?>("agent_name", name));
            _logger.LogInformation("agent_registered agentId={AgentId} name={Name}", agentId, name);

            return descriptor;
        }
    }

    /// <summary>
    /// Returns a single agent descriptor from the repository, or null if not found.
    /// </summary>
    public async Task<AgentDescriptor?> GetAgentAsync(string agentId)
    {
        return await _repository.GetAgentDescriptorAsync(agentId);
    }

    /// <summary>
    /// Returns all registered agent descriptors from the repository.
    /// </summary>
    public async Task<List<AgentDescriptor>> GetAllAgentsAsync()
    {
        return await _repository.GetAllAgentDescriptorsAsync();
    }

    /// <summary>
    /// Transitions an agent to Running state, persists the update, and broadcasts via SignalR.
    /// Throws AgentNotFoundException or InvalidStateTransitionException on invalid input.
    /// </summary>
    public async Task<AgentDescriptor> StartAgentAsync(string agentId)
    {
        using (var activity = HubTracing.StartStartAgent(agentId))
        {
            var snapshot = _registry.GetRegistrySnapshot();
            var previousStatus = snapshot.TryGetValue(agentId, out var prev) ? prev.Status.ToString() : "Unknown";

            _registry.StartAgent(agentId);

            var updated = _registry.GetRegistrySnapshot()[agentId];
            await _repository.SaveAgentDescriptorAsync(updated);

            UpdateActiveAgentCount();
            _logger.LogInformation("agent_lifecycle agentId={AgentId} event=started status={Status}", agentId, updated.Status);
            await _hubContext.Clients.All.SendAsync("AgentStatusChanged", agentId, previousStatus, updated.Status.ToString());

            return updated;
        }
    }

    /// <summary>
    /// Transitions an agent to Stopped state, persists the update, and broadcasts via SignalR.
    /// Throws AgentNotFoundException or InvalidStateTransitionException on invalid input.
    /// </summary>
    public async Task<AgentDescriptor> StopAgentAsync(string agentId)
    {
        using (var activity = HubTracing.StartStopAgent(agentId))
        {
            var snapshot = _registry.GetRegistrySnapshot();
            var previousStatus = snapshot.TryGetValue(agentId, out var prev) ? prev.Status.ToString() : "Unknown";

            _registry.StopAgent(agentId);

            var updated = _registry.GetRegistrySnapshot()[agentId];
            await _repository.SaveAgentDescriptorAsync(updated);

            UpdateActiveAgentCount();
            _logger.LogInformation("agent_lifecycle agentId={AgentId} event=stopped status={Status}", agentId, updated.Status);
            await _hubContext.Clients.All.SendAsync("AgentStatusChanged", agentId, previousStatus, updated.Status.ToString());

            return updated;
        }
    }

    /// <summary>
    /// Marks an agent as faulted, persists the update, and emits a failure metric.
    /// </summary>
    public async Task MarkAgentFaultedAsync(string agentId, string reason)
    {
        _registry.MarkAgentFaulted(agentId, reason);

        var updated = _registry.GetRegistrySnapshot()[agentId];
        await _repository.SaveAgentDescriptorAsync(updated);

        _metrics.AgentFailedTotal.Add(1, new KeyValuePair<string, object?>("agent_id", agentId), new KeyValuePair<string, object?>("reason", reason));
        UpdateActiveAgentCount();
        _logger.LogWarning("agent_faulted agentId={AgentId} reason={Reason}", agentId, reason);
    }

    private void UpdateActiveAgentCount()
    {
        var snapshot = _registry.GetRegistrySnapshot();
        var count = snapshot.Values.Count(a => a.Status == AgentStatus.Running);
        _metrics.UpdateActiveAgentCount(count);
    }
}
