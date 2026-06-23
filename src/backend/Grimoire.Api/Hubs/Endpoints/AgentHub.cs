using Microsoft.AspNetCore.SignalR;

namespace Grimoire.Api.Hubs.Endpoints;

/// <summary>SignalR hub for real-time agent status broadcasts to connected clients.</summary>
public class AgentHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Broadcasts an agent status change to all connected clients.</summary>
    public async Task SendAgentStatusChange(string agentId, string previousStatus, string currentStatus)
    {
        await Clients.All.SendAsync("AgentStatusChanged", agentId, previousStatus, currentStatus);
    }
}
