using System.Diagnostics;
using System.Text.Json;
using Grimoire.EvalRunner.Providers;

namespace Grimoire.EvalRunner.Workspace;

/// <summary>One denied tool action, read back from the NDJSON terminal event (mirrors
/// <c>Grimoire.AgentRuntime.Guardrails.DeniedActionRecord</c> without a project reference
/// to it — the eval runner reaches the agent only through its process/event-channel
/// contract, never a concrete type, per C7/C8).</summary>
public sealed record QueryEvalDeniedAction(
    string Action, string? RequestedTarget, string? CanonicalTarget, string? Reason, int Turn);

/// <summary>
/// One spawned Query agent turn's outcome. Unlike Ingest (<see cref="AgentRunResult"/>,
/// scored from a written Task Artifact), Query writes no artifact at all (R3,
/// 008-query-agent) — the answer and denied actions are read from the NDJSON
/// <c>completed</c>/<c>failed</c> terminal event on stdout (<c>RunEventEmitter</c>
/// contract, contracts/query-run-events.md).
/// </summary>
public sealed record QueryAgentRunResult(
    int ExitCode,
    bool TimedOut,
    string StdErr,
    bool Completed,
    string? Answer,
    string? FailureReason,
    IReadOnlyList<QueryEvalDeniedAction> DeniedActions,
    // ADR-015 (012-query-synthesis-writes): wiki-root-relative paths of pages this turn
    // created, read back from the terminal event's `createdPages` field.
    IReadOnlyList<string> CreatedPages);

/// <summary>
/// Spawns the real <c>Grimoire.QueryAgent</c> executable per sample through its
/// production CLI contract (ADR-011), mirroring <see cref="AgentProcessInvoker"/>'s
/// relationship to Grimoire.IngestAgent (T097, 008-query-agent). The only
/// <see cref="Process"/> user for Query in this assembly (ADR-012 C8).
/// </summary>
public sealed class QueryAgentProcessInvoker
{
    private static readonly string[] ScrubbedVariables =
    [
        "ANTHROPIC_AUTH_TOKEN",
        "GRIMOIRE_QUERY_BASE_URL",
        "GRIMOIRE_QUERY_MODEL",
        "GRIMOIRE_EVAL_PROVIDER_BASE_URL",
        "GRIMOIRE_EVAL_PROVIDER_MODEL",
        "GRIMOIRE_EVAL_PROVIDER_API_KEY",
        "GRIMOIRE_MODEL_REPLAY_PATH",
        "GRIMOIRE_MODEL_CAPTURE_PATH",
        // No OTLP export from spawned eval children — their telemetry is not production data.
        "OTEL_EXPORTER_OTLP_ENDPOINT",
    ];

    private readonly string _agentDllPath;

    public QueryAgentProcessInvoker(string agentDllPath)
    {
        _agentDllPath = agentDllPath;
    }

    public static QueryAgentProcessInvoker ForRepo(EvalPaths paths)
        => new(ResolveAgentDllPath(paths.RepoRoot));

    /// <summary>Mirrors <see cref="AgentProcessInvoker.ResolveAgentDllPath"/> for Query's own build output.</summary>
    public static string ResolveAgentDllPath(string repoRoot)
    {
        var separator = Path.DirectorySeparatorChar;
        var preferred = AppContext.BaseDirectory.Contains($"{separator}Release{separator}", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Release", "Debug" }
            : ["Debug", "Release"];

        foreach (var configuration in preferred)
        {
            var candidate = Path.Combine(
                repoRoot, "backend", "src", "Grimoire.QueryAgent", "bin", configuration, "net10.0", "Grimoire.QueryAgent.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Grimoire.QueryAgent.dll not found in its build output. Build first: dotnet build backend/Grimoire.slnx");
    }

    public async Task<QueryAgentRunResult> RunAsync(
        string turnId,
        string wikiRoot,
        string prompt,
        IReadOnlyList<(string Prompt, string Answer)> priorTurns,
        EvalPaths paths,
        AgentModelMode mode,
        TimeSpan budget,
        // ADR-015 (012-query-synthesis-writes): per-sample write-coordination lock
        // directory (contracts/query-write-scope-and-coordination.md §4) — the CLI
        // argument is required, so every caller must supply one now that Query can write.
        string writeLocksDir,
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
        AddOption(startInfo, "--turn-id", turnId);
        AddOption(startInfo, "--wiki-root", wikiRoot);
        AddOption(startInfo, "--pages-dir", Path.Combine(wikiRoot, "pages"));
        AddOption(startInfo, "--index-path", Path.Combine(wikiRoot, "index.md"));
        AddOption(startInfo, "--log-path", Path.Combine(wikiRoot, "log.md"));
        AddOption(startInfo, "--system-prompt-path", paths.QuerySystemPromptPath);
        AddOption(startInfo, "--policy-path", paths.QueryPolicyPath);
        AddOption(startInfo, "--write-locks-dir", writeLocksDir);

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
                    startInfo.Environment["GRIMOIRE_QUERY_BASE_URL"] = mode.Provider.BaseUrl!;
                    startInfo.Environment["GRIMOIRE_QUERY_MODEL"] = mode.Provider.Model!;
                    startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] =
                        Environment.GetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY") ?? string.Empty;
                    break;
                case ProviderKind.Anthropic:
                    startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] =
                        Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN") ?? string.Empty;
                    if (mode.Provider.Model is not null)
                    {
                        startInfo.Environment["GRIMOIRE_QUERY_MODEL"] = mode.Provider.Model;
                    }

                    break;
                default:
                    throw new InvalidOperationException("Capture mode requires a resolved provider configuration.");
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start the query agent process ({_agentDllPath}).");

        // Stdin JSON mirrors QueryConversationInput/AgentProcessHost.StartQueryProcess's
        // shape (case-insensitive on the read side, so casing here is not load-bearing).
        var stdinPayload = JsonSerializer.Serialize(new
        {
            prompt,
            priorTurns = priorTurns.Select((t, i) => new
            {
                position = i + 1,
                prompt = t.Prompt,
                answer = t.Answer,
                state = "completed",
            }),
        });
        await process.StandardInput.WriteAsync(stdinPayload.AsMemory(), cancellationToken);
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

            return new QueryAgentRunResult(
                ExitCode: -1, TimedOut: true, StdErr: SafeResult(stdErrTask),
                Completed: false, Answer: null, FailureReason: null, DeniedActions: [], CreatedPages: []);
        }

        var stdout = SafeResult(stdOutTask);
        var stderr = SafeResult(stdErrTask);
        var (completed, answer, failureReason, denials, createdPages) = ParseTerminalEvent(stdout);
        var relativeCreatedPages = createdPages
            .Select(canonical => Path.GetRelativePath(wikiRoot, canonical).Replace('\\', '/'))
            .ToList();

        return new QueryAgentRunResult(
            process.ExitCode, TimedOut: false, StdErr: stderr, completed, answer, failureReason, denials, relativeCreatedPages);
    }

    /// <summary>
    /// Scans the NDJSON stdout stream for the run's one terminal event (`completed` or
    /// `failed`, `RunEventEmitter.EmitCompleted`/`EmitFailed`) and extracts the answer
    /// (`summary`), the failure reason (`reason` — this is where a `ReplayMismatchException`
    /// surfaces, per Program.cs's catch-all handler), and any denied actions — there is
    /// nothing else to read the outcome from (R3: Query writes no Task-Artifact-equivalent).
    /// </summary>
    private static (bool Completed, string? Answer, string? FailureReason, IReadOnlyList<QueryEvalDeniedAction> Denials, IReadOnlyList<string> CreatedPages) ParseTerminalEvent(string stdout)
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
            if (!root.TryGetProperty("type", out var typeProperty))
            {
                continue;
            }

            var type = typeProperty.GetString();
            if (type != "completed" && type != "failed")
            {
                continue;
            }

            var summary = root.TryGetProperty("summary", out var summaryProperty)
                && summaryProperty.ValueKind == JsonValueKind.String
                ? summaryProperty.GetString()
                : null;

            var reason = root.TryGetProperty("reason", out var reasonProperty)
                && reasonProperty.ValueKind == JsonValueKind.String
                ? reasonProperty.GetString()
                : null;

            var denials = new List<QueryEvalDeniedAction>();
            if (root.TryGetProperty("deniedActions", out var deniedActions)
                && deniedActions.ValueKind == JsonValueKind.Array)
            {
                foreach (var denied in deniedActions.EnumerateArray())
                {
                    denials.Add(new QueryEvalDeniedAction(
                        Action: denied.TryGetProperty("action", out var a) ? a.GetString() ?? string.Empty : string.Empty,
                        RequestedTarget: denied.TryGetProperty("requestedTarget", out var rt) ? rt.GetString() : null,
                        CanonicalTarget: denied.TryGetProperty("canonicalTarget", out var ct) ? ct.GetString() : null,
                        Reason: denied.TryGetProperty("reason", out var r) ? r.GetString() : null,
                        Turn: denied.TryGetProperty("turn", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0));
                }
            }

            var createdPages = new List<string>();
            if (root.TryGetProperty("createdPages", out var createdPagesProperty)
                && createdPagesProperty.ValueKind == JsonValueKind.Array)
            {
                foreach (var page in createdPagesProperty.EnumerateArray())
                {
                    if (page.ValueKind == JsonValueKind.String && page.GetString() is { } value)
                    {
                        createdPages.Add(value);
                    }
                }
            }

            return (type == "completed", summary, reason, denials, createdPages);
        }

        return (false, null, null, [], []);
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
