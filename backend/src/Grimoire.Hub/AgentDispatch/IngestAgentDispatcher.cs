using System.Diagnostics;

namespace Grimoire.Hub.AgentDispatch;

public sealed class IngestAgentDispatcher : IIngestAgentDispatcher
{
    private readonly LocalSecretsLoader _secretsLoader;
    private readonly string _agentProjectPath;

    public IngestAgentDispatcher(LocalSecretsLoader secretsLoader, string agentProjectPath)
    {
        _secretsLoader = secretsLoader;
        _agentProjectPath = agentProjectPath;
    }

    public async Task<int> DispatchAsync(IngestAgentRequest request, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(_agentProjectPath);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--task-id");
        startInfo.ArgumentList.Add(request.TaskId);
        startInfo.ArgumentList.Add("--source-ref");
        startInfo.ArgumentList.Add(request.SourceRef);
        startInfo.ArgumentList.Add("--source-kind");
        startInfo.ArgumentList.Add(request.SourceKind);
        startInfo.ArgumentList.Add("--pages-dir");
        startInfo.ArgumentList.Add(request.PagesDir);
        startInfo.ArgumentList.Add("--tasks-dir");
        startInfo.ArgumentList.Add(request.TasksDir);
        startInfo.ArgumentList.Add("--index-path");
        startInfo.ArgumentList.Add(request.IndexPath);
        startInfo.ArgumentList.Add("--log-path");
        startInfo.ArgumentList.Add(request.LogPath);
        startInfo.ArgumentList.Add("--instructions-dir");
        startInfo.ArgumentList.Add(request.InstructionsDir);
        startInfo.ArgumentList.Add("--policy-path");
        startInfo.ArgumentList.Add(request.PolicyPath);

        var authToken = _secretsLoader.GetAnthropicAuthToken();
        var ingestModel = _secretsLoader.GetIngestModel();
        var ingestTokenCap = _secretsLoader.GetIngestTokenCap();
        // Build the child env by stripping credential keys from the parent env copy and
        // re-injecting only what was explicitly loaded from the secrets file (ADR-004).
        // Convert ProcessStartInfo.Environment (nullable values) to a non-nullable dict first.
        var baseEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in startInfo.Environment)
        {
            if (value is not null)
                baseEnv[key] = value;
        }

        var childEnv = BuildChildEnvironment(baseEnv, authToken, ingestModel, ingestTokenCap, Activity.Current);
        startInfo.Environment.Clear();
        foreach (var (key, value) in childEnv)
        {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ingest agent process.");

        if (request.SourceKind == "pasted_text" && !string.IsNullOrWhiteSpace(request.PastedText))
        {
            await process.StandardInput.WriteAsync(request.PastedText);
        }

        process.StandardInput.Close();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        _ = await stdOutTask;
        var stdErr = await stdErrTask;

        return process.ExitCode > 1 && !string.IsNullOrWhiteSpace(stdErr)
            ? throw new InvalidOperationException($"Ingest agent crashed: {stdErr}")
            : process.ExitCode;
    }

    /// <summary>
    /// Builds the child-process environment from <paramref name="baseEnv"/> by removing
    /// both legacy and current Anthropic credential keys, then re-injecting only if a
    /// non-null <paramref name="authToken"/> was loaded from the secrets file (ADR-004).
    /// Also propagates the current W3C trace context (<paramref name="currentActivity"/>, typically
    /// the Hub's `hub.ingest_run.trigger` span) via `TRACEPARENT`/`TRACESTATE`, so the Ingest agent
    /// process can parent its own root span to it (Constitution IV: end-to-end trace chain).
    /// Exposed internally so tests can assert both guarantees without spawning a real process.
    /// </summary>
    public static Dictionary<string, string> BuildChildEnvironment(
        IDictionary<string, string> baseEnv,
        string? authToken,
        string? ingestModel = null,
        string? ingestTokenCap = null,
        Activity? currentActivity = null)
    {
        var env = new Dictionary<string, string>(baseEnv, StringComparer.OrdinalIgnoreCase);
        env.Remove("ANTHROPIC_API_KEY");
        env.Remove("ANTHROPIC_AUTH_TOKEN");
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            env["ANTHROPIC_AUTH_TOKEN"] = authToken;
        }

        var effectiveModel = !string.IsNullOrWhiteSpace(ingestModel)
            ? ingestModel
            : (baseEnv.TryGetValue("GRIMOIRE_INGEST_MODEL", out var inheritedModel) ? inheritedModel : null);
        env.Remove("GRIMOIRE_INGEST_MODEL");
        if (!string.IsNullOrWhiteSpace(effectiveModel))
        {
            env["GRIMOIRE_INGEST_MODEL"] = effectiveModel;
        }

        var effectiveTokenCap = !string.IsNullOrWhiteSpace(ingestTokenCap)
            ? ingestTokenCap
            : (baseEnv.TryGetValue("GRIMOIRE_INGEST_TOKEN_CAP", out var inheritedTokenCap) ? inheritedTokenCap : null);
        env.Remove("GRIMOIRE_INGEST_TOKEN_CAP");
        if (!string.IsNullOrWhiteSpace(effectiveTokenCap))
        {
            env["GRIMOIRE_INGEST_TOKEN_CAP"] = effectiveTokenCap;
        }

        env.Remove("TRACEPARENT");
        env.Remove("TRACESTATE");
        // Only propagate a Recorded (sampled) parent: an unsampled TRACEPARENT makes the agent's
        // own ParentBased sampler drop `ingest_agent.run` (StartRunActivity returns null), leaving
        // Activity.Current null for the whole run and fragmenting every subsequent span into its
        // own disconnected root trace. Omitting TRACEPARENT entirely lets the agent fall back to a
        // fresh, sampled root trace instead (T076, Convergence).
        if (currentActivity is not null && currentActivity.Recorded)
        {
            env["TRACEPARENT"] = $"00-{currentActivity.TraceId}-{currentActivity.SpanId}-01";
            if (!string.IsNullOrEmpty(currentActivity.TraceStateString))
            {
                env["TRACESTATE"] = currentActivity.TraceStateString;
            }
        }

        return env;
    }
}
