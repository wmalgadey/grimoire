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
}
