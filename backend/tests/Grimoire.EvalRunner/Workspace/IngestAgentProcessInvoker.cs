using System.Diagnostics;
using System.Text.Json;
using Grimoire.EvalRunner.Providers;

namespace Grimoire.EvalRunner.Workspace;

/// <summary>Model-adapter mode for one spawned agent run (ADR-012 env contract).</summary>
public sealed record AgentModelMode
{
    private AgentModelMode(string? replayPath, string? capturePath, ProviderConfiguration? provider)
    {
        ReplayPath = replayPath;
        CapturePath = capturePath;
        Provider = provider;
    }

    public string? ReplayPath { get; }

    public string? CapturePath { get; }

    public ProviderConfiguration? Provider { get; }

    public static AgentModelMode Replay(string recordingPath) => new(recordingPath, null, null);

    public static AgentModelMode Capture(string capturePath, ProviderConfiguration provider) => new(null, capturePath, provider);
}

/// <summary>
/// Result of one spawned agent run. <see cref="FailureReason"/> is the agent's own terminal
/// `failed` event `reason` (read from stdout, per <c>RunEventEmitter</c> contract) — null when
/// the process never reached a terminal event, in which case <see cref="StdErr"/> is the only
/// diagnostic available (e.g. an unhandled crash before the CLI's own error handling runs).
/// </summary>
public sealed record AgentRunResult(int ExitCode, bool TimedOut, string StdErr, string? FailureReason = null);

/// <summary>
/// Spawns the real <c>Grimoire.IngestAgent</c> executable per sample through its
/// production CLI contract (ADR-002), with a scoped environment (ADR-004): provider
/// credentials enter only capture-mode child processes; replay-mode children get no
/// credential at all. The only <see cref="Process"/> user in this assembly (ADR-012 C8).
/// </summary>
public sealed class IngestAgentProcessInvoker
{
    private static readonly string[] ScrubbedVariables =
    [
        "ANTHROPIC_AUTH_TOKEN",
        "GRIMOIRE_INGEST_BASE_URL",
        "GRIMOIRE_INGEST_MODEL",
        "GRIMOIRE_EVAL_PROVIDER_BASE_URL",
        "GRIMOIRE_EVAL_PROVIDER_MODEL",
        "GRIMOIRE_EVAL_PROVIDER_API_KEY",
        "GRIMOIRE_MODEL_REPLAY_PATH",
        "GRIMOIRE_MODEL_CAPTURE_PATH",
        // No OTLP export from spawned eval children — their telemetry is not production data.
        "OTEL_EXPORTER_OTLP_ENDPOINT",
    ];

    private readonly string _agentDllPath;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public IngestAgentProcessInvoker(string agentDllPath)
        : this(agentDllPath, Environment.GetEnvironmentVariable)
    {
    }

    public IngestAgentProcessInvoker(string agentDllPath, Func<string, string?> getEnvironmentVariable)
    {
        _agentDllPath = agentDllPath;
        _getEnvironmentVariable = getEnvironmentVariable;
    }

    public static IngestAgentProcessInvoker ForRepo(EvalPaths paths)
        => new(ResolveAgentDllPath(paths.RepoRoot));

    public static IngestAgentProcessInvoker ForRepo(EvalPaths paths, Func<string, string?> getEnvironmentVariable)
        => new(ResolveAgentDllPath(paths.RepoRoot), getEnvironmentVariable);

    /// <summary>
    /// The agent must be launched from its OWN build output (where its deps.json resolves
    /// every dependency) — a copy inside a test host's output directory lacks assemblies
    /// the test host takes from the ASP.NET shared framework. Prefers the configuration
    /// the current process was built in.
    /// </summary>
    public static string ResolveAgentDllPath(string repoRoot)
    {
        var separator = Path.DirectorySeparatorChar;
        var preferred = AppContext.BaseDirectory.Contains($"{separator}Release{separator}", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Release", "Debug" }
            : ["Debug", "Release"];

        foreach (var configuration in preferred)
        {
            var candidate = Path.Combine(
                repoRoot, "backend", "src", "Grimoire.IngestAgent", "bin", configuration, "net10.0", "Grimoire.IngestAgent.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Grimoire.IngestAgent.dll not found in its build output. Build first: dotnet build backend/Grimoire.slnx");
    }

    public async Task<AgentRunResult> RunAsync(
        string taskId,
        string sourceRef,
        string sourceContent,
        EvalWorkspace workspace,
        AgentModelMode mode,
        string? userPrompt,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(_agentDllPath);
        AddOption(startInfo, "--task-id", taskId);
        AddOption(startInfo, "--source-ref", sourceRef);
        AddOption(startInfo, "--source-kind", "pasted_text");
        AddOption(startInfo, "--wiki-root", workspace.WikiRoot);
        AddOption(startInfo, "--content-root", workspace.WikiRoot);
        AddOption(startInfo, "--tasks-dir", workspace.TasksDir);
        AddOption(startInfo, "--index-path", workspace.IndexPath);
        AddOption(startInfo, "--log-path", workspace.LogPath);
        AddOption(startInfo, "--foundation-prompt-path", workspace.FoundationPromptPath);
        AddOption(startInfo, "--system-prompt-path", workspace.SystemPromptPath);
        AddOption(startInfo, "--default-user-prompt-path", workspace.DefaultUserPromptPath);
        AddOption(startInfo, "--policy-path", workspace.PolicyPath);
        // 012-query-synthesis-writes (ADR-015, T041): Grimoire.IngestAgent's CLI now
        // requires --write-locks-dir (its GuardedToolExecutor is constructed with the
        // shared write-coordination guard) — this invoker never passed it, so every
        // spawned Ingest eval run aborted before writing its task artifact. Real
        // regression found while re-verifying the full Grimoire.AgentEvals suite for
        // this feature's T049; fixed here rather than deferred, since it silently broke
        // ci.yml's "Run replay agent evals" gate for every Ingest scenario.
        AddOption(startInfo, "--write-locks-dir", workspace.WriteLocksDir);
        if (!string.IsNullOrWhiteSpace(userPrompt))
        {
            AddOption(startInfo, "--user-prompt", userPrompt);
        }

        foreach (var variable in ScrubbedVariables)
        {
            startInfo.Environment.Remove(variable);
        }

        if (mode.ReplayPath is not null)
        {
            startInfo.Environment["GRIMOIRE_MODEL_REPLAY_PATH"] = mode.ReplayPath;
        }
        else if (mode.CapturePath is not null && mode.Provider is not null)
        {
            startInfo.Environment["GRIMOIRE_MODEL_CAPTURE_PATH"] = mode.CapturePath;
            switch (mode.Provider.Kind)
            {
                case ProviderKind.Affordable:
                    startInfo.Environment["GRIMOIRE_INGEST_BASE_URL"] = mode.Provider.BaseUrl!;
                    startInfo.Environment["GRIMOIRE_INGEST_MODEL"] = mode.Provider.Model!;
                    startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] =
                        _getEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY") ?? string.Empty;
                    break;
                case ProviderKind.Anthropic:
                    startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] =
                        _getEnvironmentVariable("ANTHROPIC_AUTH_TOKEN") ?? string.Empty;
                    if (mode.Provider.Model is not null)
                    {
                        startInfo.Environment["GRIMOIRE_INGEST_MODEL"] = mode.Provider.Model;
                    }

                    break;
                default:
                    throw new InvalidOperationException("Capture mode requires a resolved provider configuration.");
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start the ingest agent process ({_agentDllPath}).");

        await process.StandardInput.WriteAsync(sourceContent.AsMemory(), cancellationToken);
        process.StandardInput.Close();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetCts.CancelAfter(budget);

        try
        {
            await process.WaitForExitAsync(budgetCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already exited between timeout and kill.
            }

            return new AgentRunResult(ExitCode: -1, TimedOut: true, StdErr: SafeResult(stdErrTask));
        }

        var failureReason = ParseFailureReason(SafeResult(stdOutTask));
        return new AgentRunResult(
            process.ExitCode, TimedOut: false, StdErr: SafeResult(stdErrTask), FailureReason: failureReason);
    }

    /// <summary>
    /// Reads the terminal `failed` event's `reason` field off stdout — mirrors
    /// <c>LintAgentProcessInvoker.ParseTerminalEvent</c>, narrowed to the one field this
    /// invoker's callers need (#214: capture pipelines were reporting `StdErr`, which the
    /// agent never writes to, instead of this).
    /// </summary>
    internal static string? ParseFailureReason(string stdout)
    {
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty) || typeProperty.GetString() != "failed")
            {
                continue;
            }

            return root.TryGetProperty("reason", out var reasonProperty)
                && reasonProperty.ValueKind == JsonValueKind.String
                ? reasonProperty.GetString()
                : null;
        }

        return null;
    }

    private static void AddOption(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private string SafeResult(Task<string> task)
    {
        try
        {
            return EvalProviderResolver.SanitizeErrorText(task.GetAwaiter().GetResult(), _getEnvironmentVariable);
        }
        catch
        {
            return string.Empty;
        }
    }
}
