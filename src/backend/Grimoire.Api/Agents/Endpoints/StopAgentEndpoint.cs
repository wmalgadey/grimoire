using Grimoire.Api.Agents.Services;
using Grimoire.Api.Shared.Exceptions;

namespace Grimoire.Api.Agents.Endpoints;

public static class StopAgentEndpoint
{
    public static IEndpointRouteBuilder MapStopAgent(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/agents/{agentId}/stop", async (string agentId, IAgentOrchestrationService handler) =>
        {
            try
            {
                var descriptor = await handler.StopAgentAsync(agentId);
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
