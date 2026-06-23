using Grimoire.Api.Agents.Services;

namespace Grimoire.Api.Agents.Endpoints;

public static class GetAgentEndpoint
{
    public static IEndpointRouteBuilder MapGetAgent(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/agents/{agentId}", async (string agentId, IAgentOrchestrationService handler) =>
        {
            var descriptor = await handler.GetAgentAsync(agentId);
            return descriptor is null
                ? Results.NotFound(new { error = "AgentNotFound", message = $"Agent '{agentId}' not found.", statusCode = 404 })
                : Results.Ok(descriptor);
        });
        return routes;
    }
}
