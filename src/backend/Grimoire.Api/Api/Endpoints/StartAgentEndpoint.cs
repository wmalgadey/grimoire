using Grimoire.Api.Api.Handlers;

namespace Grimoire.Api.Api.Endpoints;

/// <summary>
/// Endpoint: POST /api/agents/{agentId}/start — starts an agent (Unregistered → Running).
/// </summary>
public static class StartAgentEndpoint
{
    public static IEndpointRouteBuilder MapStartAgent(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/agents/{agentId}/start", async (string agentId, HubOrchestrationHandler handler) =>
        {
            var descriptor = await handler.StartAgentAsync(agentId);
            return Results.Ok(descriptor);
        });

        return routes;
    }
}
