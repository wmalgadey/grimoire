using System.Diagnostics;

namespace Grimoire.Hub.AgentDispatch;

/// <summary>
/// A started agent child process as seen by the run coordinator: a stream of stdout
/// lines (the NDJSON event channel) and a termination lever. Run outcome is never
/// derived from the exit code (ADR-008).
/// </summary>
public interface IAgentProcessHandle : IAsyncDisposable
{
    IAsyncEnumerable<string> ReadStdoutLinesAsync(CancellationToken cancellationToken);

    /// <summary>Forcefully terminates the agent process tree (liveness failure cleanup).</summary>
    void Terminate();
}

/// <summary>
/// Seam between the run coordinator and the real child process, so supervision and
/// queue behavior are hermetically testable with scripted event streams (Principle II).
/// </summary>
public interface IAgentProcessLauncher
{
    Task<IAgentProcessHandle> StartAsync(IngestAgentRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns the agent child-process lifecycle (ADR-002 spawn model, ADR-004 credential
/// scoping, ADR-008 event channel). This is the ONLY type in Grimoire.Hub permitted to
/// call <c>Process.WaitForExit*</c> (enforced by NonBlockingDispatchRuleTests): it waits
/// solely for post-termination cleanup and for the manual CLI run-to-exit path — never
/// to derive a run outcome on the dispatch path.
/// </summary>
public sealed class AgentProcessHost : IAgentProcessLauncher
{
    private readonly LocalSecretsLoader _secretsLoader;
    private readonly string _agentProjectPath;

    public AgentProcessHost(LocalSecretsLoader secretsLoader, string agentProjectPath)
    {
        _secretsLoader = secretsLoader;
        _agentProjectPath = agentProjectPath;
    }

    public async Task<IAgentProcessHandle> StartAsync(IngestAgentRequest request, CancellationToken cancellationToken = default)
    {
        var process = StartProcess(request);

        if (request.SourceKind == "pasted_text" && !string.IsNullOrWhiteSpace(request.PastedText))
        {
            await process.StandardInput.WriteAsync(request.PastedText);
        }

        process.StandardInput.Close();
        return new ProcessHandle(process);
    }

    /// <summary>
    /// Manual CLI path (`submit-source`): runs the agent to completion and returns the
    /// exit code. Per ADR-008 the exit code remains valid for manual CLI invocation and
    /// diagnostics; the web dispatch path never uses this method.
    /// </summary>
    public async Task<int> RunToExitAsync(IngestAgentRequest request, CancellationToken cancellationToken = default)
    {
        using var process = StartProcess(request);

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

    private Process StartProcess(IngestAgentRequest request)
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
        startInfo.ArgumentList.Add("--system-prompt-path");
        startInfo.ArgumentList.Add(request.SystemPromptPath);
        startInfo.ArgumentList.Add("--default-user-prompt-path");
        startInfo.ArgumentList.Add(request.DefaultUserPromptPath);
        startInfo.ArgumentList.Add("--policy-path");
        startInfo.ArgumentList.Add(request.PolicyPath);
        if (!string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            startInfo.ArgumentList.Add("--user-prompt");
            startInfo.ArgumentList.Add(request.UserPrompt);
        }

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

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ingest agent process.");
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

    private sealed class ProcessHandle : IAgentProcessHandle
    {
        private readonly Process _process;

        public ProcessHandle(Process process)
        {
            _process = process;
        }

        public async IAsyncEnumerable<string> ReadStdoutLinesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                if (line is null)
                {
                    // Pipe closed (process exited). Per ADR-008 this does not transition the
                    // run; silence lets the liveness window fire if no terminal event came.
                    yield break;
                }

                yield return line;
            }
        }

        public void Terminate()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    // Cleanup wait only — bounded, after termination; never outcome derivation.
                    _process.WaitForExit(5_000);
                }
            }
            catch (InvalidOperationException)
            {
                // Process already gone.
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                _process.Dispose();
            }
            catch
            {
                // best-effort
            }

            return ValueTask.CompletedTask;
        }
    }
}
