using Grimoire.Api.Api.Handlers;

namespace Grimoire.Api.Api.Endpoints;

/// <summary>
/// Endpoint: GET /api/agents — returns all registered agents.
/// </summary>
public static class ListAgentsEndpoint
{
    public static IEndpointRouteBuilder MapListAgents(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/agents", async (HubOrchestrationHandler handler) =>
        {
            var agents = await handler.GetAllAgentsAsync();
            return Results.Ok(new { agents, total = agents.Count });
        });

        return routes;
    }
}
