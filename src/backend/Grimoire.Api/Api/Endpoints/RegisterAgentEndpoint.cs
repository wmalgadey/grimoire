using Grimoire.Api.Api.Handlers;
using Grimoire.Api.Core.Exceptions;

namespace Grimoire.Api.Api.Endpoints;

/// <summary>
/// Request body for registering a new agent.
/// </summary>
public record RegisterAgentRequest(string AgentId, string Name, string[] Capabilities);

/// <summary>
/// Endpoint: POST /api/agents — registers a new agent.
/// </summary>
public static class RegisterAgentEndpoint
{
    public static IEndpointRouteBuilder MapRegisterAgent(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/agents", async (RegisterAgentRequest request, HubOrchestrationHandler handler) =>
        {
            try
            {
                var descriptor = await handler.RegisterAgentAsync(request.AgentId, request.Name, request.Capabilities);
                return Results.Created($"/api/agents/{descriptor.AgentId}", descriptor);
            }
            catch (AgentAlreadyRegisteredException ex)
            {
                return Results.Conflict(new { error = "AgentAlreadyRegistered", message = ex.Message, statusCode = 409 });
            }
        });

        return routes;
    }
}
