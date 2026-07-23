using System.Diagnostics;
using Grimoire.EvalRunner.Providers;

namespace Grimoire.EvalRunner.Workspace;

/// <summary>Model-adapter mode for one spawned agent run (ADR-011 env contract).</summary>
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

/// <summary>Result of one spawned agent run.</summary>
public sealed record AgentRunResult(int ExitCode, bool TimedOut, string StdErr);

/// <summary>
/// Spawns the real <c>Grimoire.IngestAgent</c> executable per sample through its
/// production CLI contract (ADR-002), with a scoped environment (ADR-004): provider
/// credentials enter only capture-mode child processes; replay-mode children get no
/// credential at all. The only <see cref="Process"/> user in this assembly (ADR-011 C8).
/// </summary>
public sealed class AgentProcessInvoker
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

    public AgentProcessInvoker(string agentDllPath)
    {
        _agentDllPath = agentDllPath;
    }

    public static AgentProcessInvoker ForRepo(EvalPaths paths)
        => new(ResolveAgentDllPath(paths.RepoRoot));

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
        AddOption(startInfo, "--pages-dir", workspace.PagesDir);
        AddOption(startInfo, "--tasks-dir", workspace.TasksDir);
        AddOption(startInfo, "--index-path", workspace.IndexPath);
        AddOption(startInfo, "--log-path", workspace.LogPath);
        AddOption(startInfo, "--system-prompt-path", workspace.SystemPromptPath);
        AddOption(startInfo, "--default-user-prompt-path", workspace.DefaultUserPromptPath);
        AddOption(startInfo, "--policy-path", workspace.PolicyPath);
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
                        Environment.GetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY") ?? string.Empty;
                    break;
                case ProviderKind.Anthropic:
                    startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] =
                        Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN") ?? string.Empty;
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

        _ = SafeResult(stdOutTask);
        return new AgentRunResult(process.ExitCode, TimedOut: false, StdErr: SafeResult(stdErrTask));
    }

    private static void AddOption(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private static string SafeResult(Task<string> task)
    {
        try
        {
            return EvalProviderResolver.SanitizeErrorText(task.GetAwaiter().GetResult());
        }
        catch
        {
            return string.Empty;
        }
    }
}
