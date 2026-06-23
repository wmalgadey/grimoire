using Grimoire.Api.Api.Handlers;

namespace Grimoire.Api.Api.Endpoints;

/// <summary>
/// Endpoint: GET /api/agents/{agentId} — returns a single agent descriptor by ID.
/// </summary>
public static class GetAgentEndpoint
{
    public static IEndpointRouteBuilder MapGetAgent(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/agents/{agentId}", async (string agentId, HubOrchestrationHandler handler) =>
        {
            var descriptor = await handler.GetAgentAsync(agentId);
            return descriptor is null
                ? Results.NotFound(new { error = "AgentNotFound", message = $"Agent '{agentId}' not found.", statusCode = 404 })
                : Results.Ok(descriptor);
        });

        return routes;
    }
}
