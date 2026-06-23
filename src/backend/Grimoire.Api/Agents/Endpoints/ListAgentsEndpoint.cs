using Grimoire.Api.Agents.Services;

namespace Grimoire.Api.Agents.Endpoints;

public static class ListAgentsEndpoint
{
    public static IEndpointRouteBuilder MapListAgents(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/agents", async (IAgentOrchestrationService handler) =>
        {
            var agents = await handler.GetAllAgentsAsync();
            return Results.Ok(new { agents, total = agents.Count });
        });
        return routes;
    }
}
