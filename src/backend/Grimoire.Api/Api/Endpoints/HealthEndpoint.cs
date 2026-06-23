using System.Diagnostics;
using Grimoire.Api.Core.Domain;
using Grimoire.Api.Infrastructure.Observability;
using Grimoire.Api.Infrastructure.Persistence;

namespace Grimoire.Api.Api.Endpoints;

/// <summary>
/// Endpoint: GET /health — returns hub health status based on registered agent states.
/// Returns 200 when Healthy or Unknown, 503 when any agent is Faulted (Degraded).
/// </summary>
public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/health", async (AgentRepository repository, HubMetrics metrics) =>
        {
            var sw = Stopwatch.StartNew();

            var agents = await repository.GetAllAgentDescriptorsAsync();
            var hasRunning = agents.Any(a => a.Status == AgentStatus.Running);
            var hasFaulted = agents.Any(a => a.Status == AgentStatus.Faulted);

            string overall;
            if (hasFaulted)
                overall = "Degraded";
            else if (hasRunning)
                overall = "Healthy";
            else
                overall = "Unknown";

            sw.Stop();
            metrics.HealthCheckDurationMs.Record(sw.Elapsed.TotalMilliseconds);

            var response = new
            {
                overall,
                timestamp = DateTime.UtcNow,
                agents
            };

            return overall == "Degraded"
                ? Results.Json(response, statusCode: 503)
                : Results.Ok(response);
        });

        return routes;
    }
}
