using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Grimoire.Api;
using Grimoire.Api.Agents.Persistence;
using Grimoire.Api.Ingest.Persistence;
using Grimoire.Api.Ingest.Services;
using Grimoire.Api.Tests.Stubs;

namespace Grimoire.Api.Tests.Fixtures;

public class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"grimoire-test-{Guid.NewGuid()}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var dbPath = _dbPath;
        var connectionString = $"Data Source={dbPath};Version=3;";

        builder.ConfigureServices(services =>
        {
            // Replace the shared SQLite repositories with per-test isolated ones
            ReplaceService<IngestRepository>(services, new IngestRepository(connectionString));
            ReplaceService<AgentDbInitializer>(services,
                new AgentDbInitializer(connectionString, Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentDbInitializer>.Instance));

            // Replace the real IngestAgentClient with a stub so tests
            // don't require a running ingest agent at localhost:5100
            services.AddScoped<IIngestAgentClient, StubIngestAgentClient>();
        });
    }

    private static void ReplaceService<T>(IServiceCollection services, T replacement) where T : class
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (existing != null) services.Remove(existing);
        services.AddSingleton(replacement);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
