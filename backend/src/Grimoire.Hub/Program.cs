using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.Submission;
using Grimoire.Hub;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHubTelemetry();

var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
var dbPath = Path.Combine(repoRoot, "backend", "data", "operational-state.db");
var envPath = Path.Combine(repoRoot, ".env");
var agentProjectPath = Path.Combine(repoRoot, "backend", "src", "Grimoire.IngestAgent", "Grimoire.IngestAgent.csproj");

var repository = new OperationalStateRepository(dbPath);
await repository.InitializeAsync();

var reconciler = new RestartReconciler(repository);
await reconciler.ReconcileRunningTasksAsync(repoRoot);

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

	var taskId = await service.SubmitAsync(new SubmitSourceOptions(sourcePath, sourceKind, pastedText), repoRoot);
	Console.WriteLine($"Submitted ingest task: {taskId}");
	return;
}

var app = builder.Build();
app.MapGet("/", () => "Grimoire Hub");
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

	if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
	{
		throw new InvalidOperationException($"Could not locate repository root: {error}");
	}

	return output;
}
