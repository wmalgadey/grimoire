using Grimoire.Api.Agents.Services;

namespace Grimoire.Api.Agents.Endpoints;

public static class StartAgentEndpoint
{
    public static IEndpointRouteBuilder MapStartAgent(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/agents/{agentId}/start", async (string agentId, IAgentOrchestrationService handler) =>
        {
            var descriptor = await handler.StartAgentAsync(agentId);
            return Results.Ok(descriptor);
        });
        return routes;
    }
}
