using Grimoire.Api.Agents.Services;
using Grimoire.Api.Shared.Exceptions;

namespace Grimoire.Api.Agents.Endpoints;

public record RegisterAgentRequest(string AgentId, string Name, string[] Capabilities);

public static class RegisterAgentEndpoint
{
    public static IEndpointRouteBuilder MapRegisterAgent(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/agents", async (RegisterAgentRequest request, IAgentOrchestrationService handler) =>
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
