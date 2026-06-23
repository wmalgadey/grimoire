using System.Diagnostics;
using Grimoire.Api.Agents.Models;
using Grimoire.Api.Agents.Persistence;
using Grimoire.Api.Agents.Services;
using Grimoire.Api.Shared.Observability;

namespace Grimoire.Api.Agents.Endpoints;

/// <summary>
/// GET /health — returns hub health based on agent states.
/// 200 Healthy/Unknown, 503 Degraded (any agent faulted).
/// </summary>
public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/health", async (AgentRepository repository, HubMetrics metrics, HubAgentRegistry registry) =>
        {
            var sw = Stopwatch.StartNew();
            var snapshot = registry.GetRegistrySnapshot();

            using (var activity = HubTracing.StartHealthCheck(snapshot.Count))
            {
                var agents = await repository.GetAllAgentDescriptorsAsync();
                var hasFaulted = agents.Any(a => a.Status == AgentStatus.Faulted);
                var hasRunning = agents.Any(a => a.Status == AgentStatus.Running);

                var overall = hasFaulted ? "Degraded" : hasRunning ? "Healthy" : "Unknown";

                sw.Stop();
                metrics.HealthCheckDurationMs.Record(sw.Elapsed.TotalMilliseconds);

                var response = new { overall, timestamp = DateTime.UtcNow, agents };
                return overall == "Degraded"
                    ? Results.Json(response, statusCode: 503)
                    : Results.Ok(response);
            }
        });
        return routes;
    }
}
