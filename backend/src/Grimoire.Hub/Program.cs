using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.Conversion;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.Submission;
using Grimoire.Hub.TaskArtifact;
using Grimoire.Hub;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHubTelemetry();
builder.Services.AddSignalR();
builder.Services.AddHttpClient<UrlContentFetcher>();
builder.Services.AddSingleton(sp => MarkItDownOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<MarkItDownConverter>();
builder.Services.AddSingleton<HubTaskArtifactWriter>();
builder.Services.AddSingleton<KanbanBoardProjectionStore>();

var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
var dbPath = Path.Combine(repoRoot, "backend", "data", "operational-state.db");
var envPath = Path.Combine(repoRoot, ".env");
var agentProjectPath = Path.Combine(repoRoot, "backend", "src", "Grimoire.IngestAgent", "Grimoire.IngestAgent.csproj");

if (!File.Exists(envPath))
{
    throw new FileNotFoundException($"Required .env file was not found at '{envPath}'.", envPath);
}

var contentRootDirName = ParseOption(args, "--content-root") ?? builder.Configuration["ContentRootDirName"] ?? "wiki";
var contentPaths = ContentRootPaths.Resolve(repoRoot, contentRootDirName);
var rawStoragePaths = RawStoragePaths.Resolve(repoRoot);
builder.Services.AddSingleton(rawStoragePaths);
builder.Services.AddSingleton<SourceArtifactStore>();
builder.Services.AddSingleton<IngestLifecyclePublisher>();

var repository = new OperationalStateRepository(dbPath);
await repository.InitializeAsync();
builder.Services.AddSingleton(repository);
builder.Services.AddSingleton(contentPaths);
builder.Services.AddSingleton(new LocalSecretsLoader(envPath));
builder.Services.AddSingleton<IIngestAgentDispatcher>(sp => new IngestAgentDispatcher(sp.GetRequiredService<LocalSecretsLoader>(), agentProjectPath));
builder.Services.AddSingleton<IngestRunGate>();
builder.Services.AddSingleton<IngestSubmissionValidator>();
builder.Services.AddSingleton<IngestSubmissionPipeline>();

var reconciler = new RestartReconciler(repository);
await reconciler.ReconcileRunningTasksAsync(contentPaths.TasksDir, contentPaths.LogPath);

if (args.Length > 0 && string.Equals(args[0], "submit-source", StringComparison.OrdinalIgnoreCase))
{
    var sourcePath = ParseOption(args, "--path") ?? throw new ArgumentException("Missing --path option.");
    var sourceKind = ParseOption(args, "--source-kind") ?? "file";
    string? pastedText = null;
    if (sourceKind == "pasted_text")
    {
        pastedText = await Console.In.ReadToEndAsync();
    }

    var secretsLoader = new LocalSecretsLoader(envPath);
    var dispatcher = new IngestAgentDispatcher(secretsLoader, agentProjectPath);
    var service = new SubmissionService(repository, dispatcher);

    var taskId = await service.SubmitAsync(new SubmitSourceOptions(sourcePath, sourceKind, pastedText), repoRoot, contentPaths);
    Console.WriteLine($"Submitted ingest task: {taskId}");
    return;
}

var app = builder.Build();
app.MapGet("/", () => "Grimoire Hub");
app.MapHub<IngestLifecycleHub>("/hubs/ingest-lifecycle");
app.MapGroup("/api/ingest-submissions").MapIngestSubmissionEndpoints();
app.Run();

static string? ParseOption(string[] args, string option)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static string FindRepoRoot(string start)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "git",
        WorkingDirectory = start,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("rev-parse");
    startInfo.ArgumentList.Add("--show-toplevel");

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git process.");
    var output = process.StandardOutput.ReadToEnd().Trim();
    var error = process.StandardError.ReadToEnd().Trim();
    process.WaitForExit();

    return process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)
        ? throw new InvalidOperationException($"Could not locate repository root: {error}")
        : output;
}
