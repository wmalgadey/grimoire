using Grimoire.Api.Agents.Endpoints;
using Grimoire.Api.Agents.Persistence;
using Grimoire.Api.Agents.Services;
using Grimoire.Api.Hubs.Endpoints;
using Grimoire.Api.Hubs.Handlers;
using Grimoire.Api.Shared.Middleware;
using Grimoire.Api.Shared.Observability;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.UseUtcTimestamp = true;
    options.IncludeScopes = true;
});

var dbPath = builder.Configuration["GrimoireDb:Path"] ?? "./grimoire.db";
var connectionString = $"Data Source={dbPath};Version=3;";

builder.Services.AddSingleton<HubAgentRegistry>();
builder.Services.AddSingleton<HubMetrics>();
builder.Services.AddSingleton(sp => new AgentRepository(connectionString));
builder.Services.AddSingleton(sp => new AgentDbInitializer(connectionString, sp.GetRequiredService<ILogger<AgentDbInitializer>>()));
builder.Services.AddScoped<IAgentOrchestrationService, HubOrchestrationHandler>();

builder.Services.AddSignalR();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddSource(HubTracing.Source.Name))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter(HubMetrics.MeterName));

var app = builder.Build();

app.UseExceptionHandling();

var dbInitializer = app.Services.GetRequiredService<AgentDbInitializer>();
var registry = app.Services.GetRequiredService<HubAgentRegistry>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

await dbInitializer.InitializeAsync();
await dbInitializer.RecoverStateAsync(registry);

var environment = app.Environment.EnvironmentName;
var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
logger.LogInformation("grimoire.host.started environment={Environment} version={Version}", environment, version);

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
