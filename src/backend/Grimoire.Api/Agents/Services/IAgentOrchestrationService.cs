using Grimoire.Api.Agents.Models;

namespace Grimoire.Api.Agents.Services;

public interface IAgentOrchestrationService
{
    Task<AgentDescriptor> RegisterAgentAsync(string agentId, string name, string[] capabilities);
    Task<AgentDescriptor?> GetAgentAsync(string agentId);
    Task<List<AgentDescriptor>> GetAllAgentsAsync();
    Task<AgentDescriptor> StartAgentAsync(string agentId);
    Task<AgentDescriptor> StopAgentAsync(string agentId);
    Task MarkAgentFaultedAsync(string agentId, string reason);
}
