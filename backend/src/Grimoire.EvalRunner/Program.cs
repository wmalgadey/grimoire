// Composition root for the standalone eval command (ADR-011): the only place in this
// process that constructs a concrete model adapter (the capture-time judge client) and
// binds pipelines to the recording store, workspace invoker, and telemetry.
using Grimoire.EvalRunner;
using Grimoire.EvalRunner.Capture;
using Grimoire.EvalRunner.Providers;
using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Replay;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;
using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent.AgentCore.Adapters.Anthropic;
using Microsoft.Extensions.Logging;

var (subcommand, options) = CliOptions.Parse(args);
if (subcommand is null)
{
    Console.Error.WriteLine("Usage: Grimoire.EvalRunner <capture|replay|status> [--scenario <id>]... [--samples <n>] [--recordings-root <path>] [--summary <path>]");
    return 2;
}

using var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(console => console.SingleLine = true));
var logger = loggerFactory.CreateLogger("Grimoire.EvalRunner");

var paths = EvalPaths.Discover();
var store = new RecordingStore(options.RecordingsRoot ?? paths.DefaultRecordingsRoot);
var invoker = AgentProcessInvoker.ForRepo(paths);

var scenarios = ResolveScenarios(options.Scenarios);
if (scenarios.Count == 0)
{
    Console.Error.WriteLine($"No matching scenarios. Known: {string.Join(", ", ScenarioDefinitions.All.Select(s => s.Id))}");
    return 2;
}

switch (subcommand)
{
    case "replay":
        {
            var pipeline = new ReplayPipeline(store, paths, invoker, logger);
            var results = new List<ScenarioReplayResult>();
            foreach (var scenario in scenarios)
            {
                results.Add(await pipeline.RunScenarioAsync(scenario, CancellationToken.None));
            }

            WriteSummary(options.SummaryPath, Summary.ForReplay(results));

            if (results.Any(r => r.TrustStatus != Grimoire.EvalRunner.Recording.TrustStatus.Trusted))
            {
                foreach (var result in results.Where(r => r.TrustStatus != Grimoire.EvalRunner.Recording.TrustStatus.Trusted))
                {
                    Console.Error.WriteLine($"{result.ScenarioId}: {result.TrustStatus} — {result.Detail}");
                    foreach (var sample in result.Samples.Where(s => s.TrustStatus != Grimoire.EvalRunner.Recording.TrustStatus.Trusted))
                    {
                        Console.Error.WriteLine($"  sample {sample.Sample}: {sample.TrustStatus} — {sample.Detail}");
                    }
                }

                return 3;
            }

            return results.All(r => r.IsTrustedPass) ? 0 : 1;
        }

    case "status":
        {
            var reports = scenarios.Select(s => StalenessCheck.Evaluate(s, store, paths)).ToList();
            foreach (var report in reports.Where(r => r.Status == Grimoire.EvalRunner.Recording.TrustStatus.Stale))
            {
                EvalRunnerTelemetry.RecordRecordingStale(logger, report.ScenarioId, report.ChangedFingerprints, store.ScenarioDirectory(report.ScenarioId));
            }

            WriteSummary(options.SummaryPath, Summary.ForStatus(reports));
            return reports.All(r => r.Status == Grimoire.EvalRunner.Recording.TrustStatus.Trusted) ? 0 : 3;
        }

    case "capture":
        {
            var gate = EvalProviderResolver.Resolve();
            EvalObservability.RecordGateResolution(logger, gate);
            if (gate.Status != EvalGateStatus.Enabled)
            {
                Console.Error.WriteLine(gate.Reason);
                return 2;
            }

            var sampleCount = options.Samples ?? ScenarioDefinitions.ResolveSampleCount();
            var pipeline = new CapturePipeline(store, paths, invoker, logger, CreateJudgeClient);
            var results = new List<CaptureScenarioResult>();
            try
            {
                foreach (var scenario in scenarios)
                {
                    results.Add(await pipeline.RunScenarioAsync(scenario, gate.Configuration, sampleCount, CancellationToken.None));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(EvalProviderResolver.SanitizeErrorText($"Capture failed: {ex.Message}"));
                return 2;
            }

            WriteSummary(options.SummaryPath, Summary.ForCapture(results));

            if (results.Any(r => !r.Stored))
            {
                foreach (var result in results.Where(r => !r.Stored))
                {
                    Console.Error.WriteLine($"{result.ScenarioId}: {result.Detail}");
                }

                return 2;
            }

            return results.All(r => r.ThresholdMet && r.NoOutOfScopeGuaranteeHeld) ? 0 : 1;
        }

    default:
        Console.Error.WriteLine($"Unknown subcommand '{subcommand}'.");
        return 2;
}

static IReadOnlyList<ScenarioDefinition> ResolveScenarios(IReadOnlyList<string> requested)
    => requested.Count == 0
        ? ScenarioDefinitions.All
        : requested.Select(ScenarioDefinitions.Find).Where(s => s is not null).Cast<ScenarioDefinition>().ToList();

static void WriteSummary(string? path, string summary)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        Console.Out.WriteLine(summary);
    }
    else
    {
        File.WriteAllText(path, summary);
    }
}

// Judge client for capture-time judge scoring: the resolved provider's adapter is
// constructed here — and only here — via the same env-shim pattern the pre-009 harness
// used (the AnthropicModelClient constructor reads its env vars once, synchronously),
// then wrapped in the 120s timeout decorator (007 FR-013).
static IModelClient CreateJudgeClient(ProviderConfiguration configuration)
{
    if (configuration.Kind != ProviderKind.Affordable)
    {
        return new TimeoutEnforcingModelClient(new AnthropicModelClient());
    }

    var originalBaseUrl = Environment.GetEnvironmentVariable("GRIMOIRE_INGEST_BASE_URL");
    var originalModel = Environment.GetEnvironmentVariable("GRIMOIRE_INGEST_MODEL");
    var originalToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");

    Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_BASE_URL", configuration.BaseUrl);
    Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_MODEL", configuration.Model);
    Environment.SetEnvironmentVariable(
        "ANTHROPIC_AUTH_TOKEN",
        Environment.GetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY"));

    try
    {
        return new TimeoutEnforcingModelClient(new AnthropicModelClient());
    }
    finally
    {
        Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_BASE_URL", originalBaseUrl);
        Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_MODEL", originalModel);
        Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", originalToken);
    }
}

/// <summary>Parsed CLI options per contracts/eval-cli.md.</summary>
internal sealed record CliOptions(
    IReadOnlyList<string> Scenarios,
    int? Samples,
    string? RecordingsRoot,
    string? SummaryPath)
{
    public static (string? Subcommand, CliOptions Options) Parse(string[] args)
    {
        string? subcommand = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0].ToLowerInvariant()
            : null;

        var scenarios = new List<string>();
        int? samples = null;
        string? recordingsRoot = null;
        string? summaryPath = null;

        for (var i = subcommand is null ? 0 : 1; i < args.Length - 1; i += 2)
        {
            switch (args[i])
            {
                case "--scenario":
                    scenarios.Add(args[i + 1]);
                    break;
                case "--samples" when int.TryParse(args[i + 1], out var parsed):
                    samples = Math.Clamp(parsed, 1, 20);
                    break;
                case "--recordings-root":
                    recordingsRoot = args[i + 1];
                    break;
                case "--summary":
                    summaryPath = args[i + 1];
                    break;
            }
        }

        return (subcommand, new CliOptions(scenarios, samples, recordingsRoot, summaryPath));
    }
}
