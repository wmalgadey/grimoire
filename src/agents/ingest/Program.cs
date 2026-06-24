using Grimoire.Ingest.Api;
using Grimoire.Ingest.Cache;
using Grimoire.Ingest.Conversation;
using Grimoire.Ingest.Git;
using Grimoire.Ingest.Hub;
using Grimoire.Ingest.Pipeline;
using Grimoire.Ingest.Services;
using Grimoire.Ingest.Watcher;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// JSON console logging
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.UseUtcTimestamp = true;
    options.IncludeScopes = true;
});

// Configuration: allow env vars to override (INGEST_HTTP_PORT, INGEST_HUB_URL, INGEST_SOURCE_DIR, etc.)
builder.Configuration
    .AddEnvironmentVariables();

// HTTP port
var port = builder.Configuration["IngestHttpPort"]
    ?? builder.Configuration["INGEST_HTTP_PORT"]
    ?? "5100";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// SQLite connection string
var dbPath = builder.Configuration["IngestDbPath"]
    ?? builder.Configuration["INGEST_DB_PATH"]
    ?? "./ingest-cache.db";
var connectionString = $"Data Source={dbPath};Version=3;";

// Register Cache layer
builder.Services.AddSingleton(_ => new IngestCacheRepository(connectionString));
builder.Services.AddSingleton<IngestCache>();

// Register Git
builder.Services.AddSingleton<IngestGitService>();

// Register Hub
builder.Services.AddHttpClient("hub");
builder.Services.AddSingleton<HubClient>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HubClient>());
builder.Services.AddSingleton<HubReporter>();

// Register Pipeline
builder.Services.AddSingleton<Chunker>();
builder.Services.AddSingleton<IngestMetrics>();
builder.Services.AddSingleton<LlmAnalyzer>();
builder.Services.AddSingleton<Indexer>();
builder.Services.AddSingleton<IngestPipeline>();

// Register Conversation
builder.Services.AddSingleton<ConversationService>();

// Register Services
builder.Services.AddSingleton<IngestService>();

// Register Watcher (scoped IngestService access via IServiceProvider)
builder.Services.AddHostedService<SourceWatcher>();

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddSource(IngestTracing.Source.Name))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter(IngestMetrics.MeterName));

var app = builder.Build();

// Initialize SQLite schema
var cacheRepository = app.Services.GetRequiredService<IngestCacheRepository>();
await cacheRepository.InitializeAsync();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var environment = app.Environment.EnvironmentName;
var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";
logger.LogInformation(
    "grimoire.ingest.started environment={Environment} version={Version} port={Port}",
    environment, version, port);

// Map API endpoints
app.MapGet("/", () => "Grimoire Ingest Agent");
app.MapHealth();
app.MapTriggerRun();
app.MapFeedback();
app.MapConversation();

await app.RunAsync();

logger.LogInformation("grimoire.ingest.stopped environment={Environment}", environment);
