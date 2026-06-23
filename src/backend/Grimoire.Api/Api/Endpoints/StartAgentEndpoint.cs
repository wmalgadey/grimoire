using Grimoire.Api.Api.Handlers;
using Grimoire.Api.Core.Exceptions;

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
            try
            {
                var descriptor = await handler.StartAgentAsync(agentId);
                return Results.Ok(descriptor);
            }
            catch (AgentNotFoundException ex)
            {
                return Results.NotFound(new { error = "AgentNotFound", message = ex.Message, statusCode = 404 });
            }
            catch (InvalidStateTransitionException ex)
            {
                return Results.BadRequest(new { error = "InvalidStateTransition", message = ex.Message, statusCode = 400 });
            }
        });

        return routes;
    }
}
