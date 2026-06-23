using System.Diagnostics;
using Grimoire.Api.Agents.Models;
using Grimoire.Api.Agents.Persistence;
using Grimoire.Api.Agents.Services;
using Grimoire.Api.Hubs.Endpoints;
using Grimoire.Api.Shared.Observability;
using Microsoft.AspNetCore.SignalR;

namespace Grimoire.Api.Hubs.Handlers;

public class HubOrchestrationHandler : IAgentOrchestrationService
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
            _metrics.AgentRegisteredTotal.Add(1,
                new KeyValuePair<string, object?>("agent_id", agentId),
                new KeyValuePair<string, object?>("agent_name", name));
            _logger.LogInformation("agent_registered agentId={AgentId} name={Name}", agentId, name);
            return descriptor;
        }
    }

    public async Task<AgentDescriptor?> GetAgentAsync(string agentId)
        => await _repository.GetAgentDescriptorAsync(agentId);

    public async Task<List<AgentDescriptor>> GetAllAgentsAsync()
        => await _repository.GetAllAgentDescriptorsAsync();

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

    public async Task MarkAgentFaultedAsync(string agentId, string reason)
    {
        _registry.MarkAgentFaulted(agentId, reason);
        var updated = _registry.GetRegistrySnapshot()[agentId];
        await _repository.SaveAgentDescriptorAsync(updated);
        _metrics.AgentFailedTotal.Add(1,
            new KeyValuePair<string, object?>("agent_id", agentId),
            new KeyValuePair<string, object?>("reason", reason));
        UpdateActiveAgentCount();
        _logger.LogWarning("agent_faulted agentId={AgentId} reason={Reason}", agentId, reason);
    }

    private void UpdateActiveAgentCount()
    {
        var count = _registry.GetRegistrySnapshot().Values.Count(a => a.Status == AgentStatus.Running);
        _metrics.UpdateActiveAgentCount(count);
    }
}
