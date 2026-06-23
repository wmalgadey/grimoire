using Grimoire.Api.Api.Endpoints;
using Grimoire.Api.Api.Hubs;
using Grimoire.Api.Api.Handlers;
using Grimoire.Api.Api.Middleware;
using Grimoire.Api.Core.Domain;
using Grimoire.Api.Infrastructure.Observability;
using Grimoire.Api.Infrastructure.Persistence;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logging
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.UseUtcTimestamp = true;
    options.IncludeScopes = true;
});

// SQLite configuration
var dbPath = builder.Configuration["GrimoireDb:Path"] ?? "./grimoire.db";
var connectionString = $"Data Source={dbPath};Version=3;";

// Domain services (singleton for in-memory registry)
builder.Services.AddSingleton<HubAgentRegistry>();
builder.Services.AddSingleton<HubMetrics>();
builder.Services.AddSingleton(sp => new AgentRepository(connectionString));
builder.Services.AddSingleton(sp => new AgentDbInitializer(connectionString, sp.GetRequiredService<ILogger<AgentDbInitializer>>()));

// Application handlers
builder.Services.AddScoped<HubOrchestrationHandler>();

// ASP.NET Core services
builder.Services.AddSignalR();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddSource(HubTracing.Source.Name))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter(HubMetrics.MeterName));

var app = builder.Build();

// Middleware
app.UseExceptionHandling();

// Initialize SQLite on startup and recover state
var dbInitializer = app.Services.GetRequiredService<AgentDbInitializer>();
var registry = app.Services.GetRequiredService<HubAgentRegistry>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

await dbInitializer.InitializeAsync();
await dbInitializer.RecoverStateAsync(registry);

var environment = app.Environment.EnvironmentName;
var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
logger.LogInformation("grimoire.host.started environment={Environment} version={Version}", environment, version);

// Map routes
app.MapGet("/", () => "Grimoire API");
app.MapHub<AgentHub>("/hubs/agents");

app.MapRegisterAgent();
app.MapListAgents();
app.MapGetAgent();
app.MapStartAgent();
app.MapStopAgent();
app.MapHealth();

await app.RunAsync();

logger.LogInformation("grimoire.host.stopped environment={Environment}", environment);


