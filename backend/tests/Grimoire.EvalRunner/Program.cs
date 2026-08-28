// Composition root for the standalone eval command (ADR-012): the only place in this
// process that constructs a concrete model adapter (the capture-time judge client) and
// binds pipelines to the recording store, workspace invoker, and telemetry.
using Grimoire.EvalRunner;
using Grimoire.EvalRunner.Capture;
using Grimoire.EvalRunner.Providers;
using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Replay;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Microsoft.Extensions.Logging;

var parsed = CliOptions.Parse(args);
if (parsed.Error is not null)
{
    Console.Error.WriteLine(parsed.Error);
    Console.Error.WriteLine(CliOptions.Usage);
    return 2;
}

var subcommand = parsed.Subcommand!;
var options = parsed.Options!;

using var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(console => console.SingleLine = true));
var logger = loggerFactory.CreateLogger("Grimoire.EvalRunner");

var paths = EvalPaths.Discover();
var store = new RecordingStore(paths.RecordingsRoot);
var invoker = IngestAgentProcessInvoker.ForRepo(paths);
var queryInvoker = QueryAgentProcessInvoker.ForRepo(paths);
var lintInvoker = LintAgentProcessInvoker.ForRepo(paths);

var scenarios = ResolveScenarios(options.Scenarios);
var queryScenarios = ResolveQueryScenarios(options.Scenarios);
var lintScenarios = ResolveLintScenarios(options.Scenarios);
var remediationScenarios = ResolveRemediationReVerificationScenarios(options.Scenarios);
var knownScenarioIds = IngestScenarioDefinitions.All.Select(s => s.Id)
    .Concat(QueryScenarioDefinitions.All.Select(s => s.Id))
    .Concat(LintScenarioDefinitions.All.Select(s => s.Id))
    .Concat(RemediationReVerificationScenarioDefinitions.All.Select(s => s.Id))
    .ToList();

// Each family's resolver drops the ids it does not own — that is how one flat --scenario
// list feeds four families. It also used to swallow a typo: `--scenario lint-defcts-found`
// simply ran nothing for Lint, and paired with a valid id it ran a SHORTER capture than
// asked for, silently. An id no family knows is a wrong parameter, so it fails the run.
var unknownScenarioIds = options.Scenarios
    .Where(requested => !knownScenarioIds.Contains(requested, StringComparer.Ordinal))
    .ToList();
if (unknownScenarioIds.Count > 0)
{
    Console.Error.WriteLine(
        $"Unknown scenario id(s): {string.Join(", ", unknownScenarioIds)}. Known: {string.Join(", ", knownScenarioIds)}");
    return 2;
}

if (scenarios.Count == 0 && queryScenarios.Count == 0 && lintScenarios.Count == 0 && remediationScenarios.Count == 0)
{
    Console.Error.WriteLine($"No matching scenarios. Known: {string.Join(", ", knownScenarioIds)}");
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

            var queryPipeline = new QueryReplayPipeline(store, paths, queryInvoker, logger);
            var queryResults = new List<QueryScenarioReplayResult>();
            foreach (var scenario in queryScenarios)
            {
                queryResults.Add(await queryPipeline.RunScenarioAsync(scenario, CancellationToken.None));
            }

            var lintPipeline = new LintReplayPipeline(store, paths, lintInvoker, logger);
            var lintResults = new List<Grimoire.EvalRunner.Replay.LintScenarioReplayResult>();
            foreach (var scenario in lintScenarios)
            {
                lintResults.Add(await lintPipeline.RunScenarioAsync(scenario, CancellationToken.None));
            }

            // T039 (015-lint-board-parity, FR-018): remediation-execution re-verification —
            // reuses the same Lint-binary invoker (research.md R8, one binary, several
            // invocation modes), its own scenario/replay pipeline pair.
            var remediationPipeline = new Grimoire.EvalRunner.Replay.RemediationReVerificationReplayPipeline(store, paths, lintInvoker, logger);
            var remediationResults = new List<Grimoire.EvalRunner.Replay.RemediationReVerificationScenarioReplayResult>();
            foreach (var scenario in remediationScenarios)
            {
                remediationResults.Add(await remediationPipeline.RunScenarioAsync(scenario, CancellationToken.None));
            }

            WriteSummary(
                options.SummaryPath,
                Summary.ForReplay(results) + Summary.ForQueryReplay(queryResults) + Summary.ForLintReplay(lintResults)
                    + Summary.ForRemediationReVerificationReplay(remediationResults));

            var untrusted = results.Where(r => r.TrustStatus != Grimoire.EvalRunner.Recording.TrustStatus.Trusted).ToList();
            var queryUntrusted = queryResults.Where(r => r.TrustStatus != Grimoire.EvalRunner.Recording.TrustStatus.Trusted).ToList();
            var lintUntrusted = lintResults.Where(r => r.TrustStatus != Grimoire.EvalRunner.Recording.TrustStatus.Trusted).ToList();
            var remediationUntrusted = remediationResults.Where(r => r.TrustStatus != Grimoire.EvalRunner.Recording.TrustStatus.Trusted).ToList();
            if (untrusted.Count > 0 || queryUntrusted.Count > 0 || lintUntrusted.Count > 0 || remediationUntrusted.Count > 0)
            {
                foreach (var result in untrusted)
                {
                    Console.Error.WriteLine($"{result.ScenarioId}: {result.TrustStatus} — {result.Detail}");
                    foreach (var sample in result.Samples.Where(s => s.TrustStatus != Grimoire.EvalRunner.Recording.TrustStatus.Trusted))
                    {
                        Console.Error.WriteLine($"  sample {sample.Sample}: {sample.TrustStatus} — {sample.Detail}");
                    }
                }

                foreach (var result in queryUntrusted)
                {
                    Console.Error.WriteLine($"{result.ScenarioId}: {result.TrustStatus} — {result.Detail}");
                    foreach (var sample in result.Samples.Where(s => s.TrustStatus != Grimoire.EvalRunner.Recording.TrustStatus.Trusted))
                    {
                        Console.Error.WriteLine($"  sample {sample.Sample}: {sample.TrustStatus} — {sample.Detail}");
                    }
                }

                foreach (var result in lintUntrusted)
                {
                    Console.Error.WriteLine($"{result.ScenarioId}: {result.TrustStatus} — {result.Detail}");
                    foreach (var sample in result.Samples.Where(s => s.TrustStatus != Grimoire.EvalRunner.Recording.TrustStatus.Trusted))
                    {
                        Console.Error.WriteLine($"  sample {sample.Sample}: {sample.TrustStatus} — {sample.Detail}");
                    }
                }

                foreach (var result in remediationUntrusted)
                {
                    Console.Error.WriteLine($"{result.ScenarioId}: {result.TrustStatus} — {result.Detail}");
                    foreach (var sample in result.Samples.Where(s => s.TrustStatus != Grimoire.EvalRunner.Recording.TrustStatus.Trusted))
                    {
                        Console.Error.WriteLine($"  sample {sample.Sample}: {sample.TrustStatus} — {sample.Detail}");
                    }
                }

                return 3;
            }

            return results.All(r => r.IsTrustedPass) && queryResults.All(r => r.IsTrustedPass) && lintResults.All(r => r.IsTrustedPass)
                    && remediationResults.All(r => r.IsTrustedPass)
                ? 0
                : 1;
        }

    case "status":
        {
            var reports = scenarios.Select(s => StalenessCheck.Evaluate(s, store, paths))
                .Concat(queryScenarios.Select(s => Grimoire.EvalRunner.Recording.QueryStalenessCheck.Evaluate(s, store, paths)))
                .Concat(lintScenarios.Select(s => Grimoire.EvalRunner.Recording.LintStalenessCheck.Evaluate(s, store, paths)))
                .Concat(remediationScenarios.Select(s => Grimoire.EvalRunner.Recording.RemediationReVerificationStalenessCheck.Evaluate(s, store, paths)))
                .ToList();
            foreach (var report in reports.Where(r => r.Status == Grimoire.EvalRunner.Recording.TrustStatus.Stale))
            {
                EvalRunnerTelemetry.RecordRecordingStale(logger, report.ScenarioId, report.ChangedFingerprints, store.ScenarioDirectory(report.ScenarioId));
            }

            WriteSummary(options.SummaryPath, Summary.ForStatus(reports));
            return reports.All(r => r.Status == Grimoire.EvalRunner.Recording.TrustStatus.Trusted) ? 0 : 3;
        }

    case "capture":
        {
            LocalEnvFile.ApplyIfPresent(paths.LocalEnvPath);
            var gate = EvalProviderResolver.Resolve();
            EvalObservability.RecordGateResolution(logger, gate);
            if (gate.Status != EvalGateStatus.Enabled)
            {
                Console.Error.WriteLine(gate.Reason);
                return 2;
            }

            var sampleCount = options.Samples ?? IngestScenarioDefinitions.ResolveSampleCount();
            var parallelSamples = options.Parallelism ?? CaptureParallelism.Default;
            logger.LogInformation(
                "Capturing up to {ParallelSamples} sample(s) of a scenario concurrently (--parallel).", parallelSamples);
            var pipeline = new IngestCapturePipeline(store, paths, invoker, logger, CreateJudgeClient, parallelSamples);
            var results = new List<CaptureScenarioResult>();
            var queryPipeline = new QueryCapturePipeline(store, paths, queryInvoker, logger, parallelSamples);
            var queryResults = new List<QueryCaptureScenarioResult>();
            var lintPipeline = new LintCapturePipeline(store, paths, lintInvoker, logger, parallelSamples);
            var lintResults = new List<Grimoire.EvalRunner.Capture.LintCaptureScenarioResult>();
            var remediationPipeline = new Grimoire.EvalRunner.Capture.RemediationReVerificationCapturePipeline(
                store, paths, lintInvoker, logger, parallelSamples);
            var remediationResults = new List<Grimoire.EvalRunner.Capture.RemediationReVerificationCaptureScenarioResult>();
            try
            {
                foreach (var scenario in scenarios)
                {
                    results.Add(await pipeline.RunScenarioAsync(scenario, gate.Configuration, sampleCount, CancellationToken.None));
                }

                foreach (var scenario in queryScenarios)
                {
                    queryResults.Add(await queryPipeline.RunScenarioAsync(scenario, gate.Configuration, sampleCount, CancellationToken.None));
                }

                foreach (var scenario in lintScenarios)
                {
                    lintResults.Add(await lintPipeline.RunScenarioAsync(scenario, gate.Configuration, sampleCount, CancellationToken.None));
                }

                foreach (var scenario in remediationScenarios)
                {
                    remediationResults.Add(await remediationPipeline.RunScenarioAsync(scenario, gate.Configuration, sampleCount, CancellationToken.None));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(EvalProviderResolver.SanitizeErrorText($"Capture failed: {ex.Message}"));
                return 2;
            }

            WriteSummary(
                options.SummaryPath,
                Summary.ForCapture(results) + Summary.ForQueryCapture(queryResults) + Summary.ForLintCapture(lintResults)
                    + Summary.ForRemediationReVerificationCapture(remediationResults));

            var notStored = results.Where(r => !r.Stored).ToList();
            var queryNotStored = queryResults.Where(r => !r.Stored).ToList();
            var lintNotStored = lintResults.Where(r => !r.Stored).ToList();
            var remediationNotStored = remediationResults.Where(r => !r.Stored).ToList();
            if (notStored.Count > 0 || queryNotStored.Count > 0 || lintNotStored.Count > 0 || remediationNotStored.Count > 0)
            {
                foreach (var result in notStored)
                {
                    Console.Error.WriteLine($"{result.ScenarioId}: {result.Detail}");
                }

                foreach (var result in queryNotStored)
                {
                    Console.Error.WriteLine($"{result.ScenarioId}: {result.Detail}");
                }

                foreach (var result in lintNotStored)
                {
                    Console.Error.WriteLine($"{result.ScenarioId}: {result.Detail}");
                    // The scenario-level line only says a sample was lost, never which one or
                    // why — and a capture that loses one sample out of ten discards the other
                    // nine (no partial stores), so the per-sample reason is the only thing that
                    // tells an operator whether to simply re-run or to go fix something.
                    foreach (var sample in result.Samples.Where(s => !s.Captured))
                    {
                        Console.Error.WriteLine($"  sample {sample.Sample}: {sample.Detail}");
                    }
                }

                foreach (var result in remediationNotStored)
                {
                    Console.Error.WriteLine($"{result.ScenarioId}: {result.Detail}");
                    foreach (var sample in result.Samples.Where(s => !s.Captured))
                    {
                        Console.Error.WriteLine($"  sample {sample.Sample}: {sample.Detail}");
                    }
                }

                return 2;
            }

            return results.All(r => r.ThresholdMet && r.NoOutOfScopeGuaranteeHeld)
                && queryResults.All(r => r.ThresholdMet)
                && lintResults.All(r => r.ThresholdMet)
                && remediationResults.All(r => r.ThresholdMet)
                ? 0
                : 1;
        }

    default:
        Console.Error.WriteLine($"Unknown subcommand '{subcommand}'.");
        return 2;
}

static IReadOnlyList<ScenarioDefinition> ResolveScenarios(IReadOnlyList<string> requested)
    => requested.Count == 0
        ? IngestScenarioDefinitions.All
        : requested.Select(IngestScenarioDefinitions.Find).Where(s => s is not null).Cast<ScenarioDefinition>().ToList();

static IReadOnlyList<QueryScenarioDefinition> ResolveQueryScenarios(IReadOnlyList<string> requested)
    => requested.Count == 0
        ? QueryScenarioDefinitions.All
        : requested.Select(QueryScenarioDefinitions.Find).Where(s => s is not null).Cast<QueryScenarioDefinition>().ToList();

static IReadOnlyList<LintScenarioDefinition> ResolveLintScenarios(IReadOnlyList<string> requested)
    => requested.Count == 0
        ? LintScenarioDefinitions.DefaultSet
        : requested.Select(LintScenarioDefinitions.Find).Where(s => s is not null).Cast<LintScenarioDefinition>().ToList();

static IReadOnlyList<RemediationReVerificationScenarioDefinition> ResolveRemediationReVerificationScenarios(IReadOnlyList<string> requested)
    => requested.Count == 0
        ? RemediationReVerificationScenarioDefinitions.All
        : requested.Select(RemediationReVerificationScenarioDefinitions.Find)
            .Where(s => s is not null).Cast<RemediationReVerificationScenarioDefinition>().ToList();

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

/// <summary>
/// Parsed CLI options per contracts/eval-cli.md. No <c>--recordings-root</c> switch
/// (ADR-022/FR-016/SC-009): recordings always resolve from the fixture folder inside the
/// test project via <see cref="EvalPaths.RecordingsRoot"/>, independent of hub
/// configuration.
/// </summary>
internal sealed record CliOptions(
    IReadOnlyList<string> Scenarios,
    int? Samples,
    int? Parallelism,
    string? SummaryPath)
{
    public const string Usage =
        "Usage: Grimoire.EvalRunner <capture|replay|status> [--scenario <id>]... [--samples <n>] "
        + "[--parallel <n>] [--summary <path>]";

    // Issue: a stray token used to be skipped silently AND — because the loop walked args
    // in fixed pairs — shifted every following option onto an odd index, where none of
    // them matched either. `capture --no-build --scenario <id>` therefore
    // parsed as "no scenario filter at all", and an empty filter means EVERY scenario:
    // one misplaced `dotnet run` flag turned a seven-scenario refresh into a live
    // re-capture of the whole corpus against the provider. Nothing is worth silently
    // ignoring here — every unrecognized or value-less argument now fails the run before
    // a single provider call is made.
    public static CliParseResult Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return CliParseResult.Failed("No subcommand given.");
        }

        var subcommand = !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0].ToLowerInvariant()
            : null;
        if (subcommand is null)
        {
            return CliParseResult.Failed($"Expected a subcommand as the first argument, got '{args[0]}'.");
        }

        var scenarios = new List<string>();
        int? samples = null;
        int? parallelism = null;
        string? summaryPath = null;

        for (var i = 1; i < args.Length; i++)
        {
            var name = args[i];
            if (!RequiresValue(name))
            {
                return CliParseResult.Failed($"Unrecognized argument '{name}'.");
            }

            if (i + 1 >= args.Length)
            {
                return CliParseResult.Failed($"Option '{name}' requires a value.");
            }

            var value = args[++i];
            if (value.StartsWith("--", StringComparison.Ordinal))
            {
                return CliParseResult.Failed($"Option '{name}' requires a value, but was followed by '{value}'.");
            }

            switch (name)
            {
                case "--scenario":
                    scenarios.Add(value);
                    break;
                case "--samples":
                    if (!int.TryParse(value, out var parsed))
                    {
                        return CliParseResult.Failed($"Option '--samples' requires an integer, got '{value}'.");
                    }

                    // Out-of-range values stay clamped rather than rejected: eval.yml
                    // documents "clamped 1-20" as the contract for its own sample-count
                    // input, and a number is a well-formed value — unlike the cases above,
                    // there is nothing here the caller could have meant instead.
                    samples = Math.Clamp(parsed, 1, 20);
                    break;
                case "--parallel":
                    if (!int.TryParse(value, out var requestedParallelism))
                    {
                        return CliParseResult.Failed($"Option '--parallel' requires an integer, got '{value}'.");
                    }

                    // Unlike --samples, an out-of-range value is rejected rather than
                    // clamped: nothing documents a clamp for this switch, and silently
                    // running 16 concurrent agents for someone who typed 100 is exactly
                    // the kind of quiet reinterpretation this parser exists to stop.
                    if (requestedParallelism < CaptureParallelism.Sequential || requestedParallelism > CaptureParallelism.Max)
                    {
                        return CliParseResult.Failed(
                            $"Option '--parallel' must be between {CaptureParallelism.Sequential} and {CaptureParallelism.Max}, got '{value}'.");
                    }

                    parallelism = requestedParallelism;
                    break;
                case "--summary":
                    summaryPath = value;
                    break;
            }
        }

        return CliParseResult.Parsed(subcommand, new CliOptions(scenarios, samples, parallelism, summaryPath));
    }

    private static bool RequiresValue(string name)
        => name is "--scenario" or "--samples" or "--parallel" or "--summary";
}

/// <summary>
/// Outcome of <see cref="CliOptions.Parse"/>: either a subcommand with its options, or the
/// operator-facing reason the argument list was rejected. Carrying the reason (rather than
/// returning a null subcommand) is what lets the composition root name the offending
/// argument instead of printing bare usage.
/// </summary>
internal sealed record CliParseResult(string? Subcommand, CliOptions? Options, string? Error)
{
    public static CliParseResult Parsed(string subcommand, CliOptions options)
        => new(subcommand, options, Error: null);

    public static CliParseResult Failed(string error) => new(Subcommand: null, Options: null, error);
}
