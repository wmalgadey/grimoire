using Grimoire.Api.Ingest.Models.SignalREvents;
using Microsoft.AspNetCore.SignalR;

namespace Grimoire.Api.Ingest.Hubs;

public class IngestHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }

    public async Task IngestRunStartedAsync(IngestRunStarted payload)
    {
        await Clients.All.SendAsync("IngestRunStarted", payload);
    }

    public async Task IngestProgressAsync(IngestProgress payload)
    {
        await Clients.All.SendAsync("IngestProgress", payload);
    }

    public async Task IngestLogEntryAsync(IngestLogEntry payload)
    {
        await Clients.All.SendAsync("IngestLogEntry", payload);
    }

    public async Task IngestFeedbackRequestAsync(IngestFeedbackRequest payload)
    {
        await Clients.All.SendAsync("IngestFeedbackRequest", payload);
    }

    public async Task IngestRunCompletedAsync(IngestRunCompleted payload)
    {
        await Clients.All.SendAsync("IngestRunCompleted", payload);
    }

    public async Task IngestConversationOpenedAsync(IngestConversationOpened payload)
    {
        await Clients.All.SendAsync("IngestConversationOpened", payload);
    }

    public async Task IngestConversationTurnAsync(IngestConversationTurn payload)
    {
        await Clients.All.SendAsync("IngestConversationTurn", payload);
    }
}
