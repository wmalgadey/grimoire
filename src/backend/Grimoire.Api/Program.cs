using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation());

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var environment = app.Environment.EnvironmentName;
var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";

logger.LogInformation("grimoire.host.started environment={Environment} version={Version}", environment, version);

app.MapGet("/", () => "Grimoire API");

await app.RunAsync();

logger.LogInformation("grimoire.host.stopped environment={Environment}", environment);
