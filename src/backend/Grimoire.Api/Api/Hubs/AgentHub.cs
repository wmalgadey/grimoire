using Microsoft.AspNetCore.SignalR;

namespace Grimoire.Api.Api.Hubs;

/// <summary>
/// SignalR hub for real-time agent status broadcasts to connected clients.
/// </summary>
public class AgentHub : Hub
{
    /// <summary>
    /// Broadcasts an agent status change to all connected clients.
    /// </summary>
    public async Task SendAgentStatusChange(string agentId, string previousStatus, string currentStatus)
    {
        await Clients.All.SendAsync("AgentStatusChanged", agentId, previousStatus, currentStatus);
    }
}
