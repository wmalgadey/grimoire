using System.Diagnostics;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.RemediationTasks;

namespace Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;

using Grimoire.Hub.AgentDispatch;

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
    private readonly string _ingestAgentWorkerPath;
    private readonly string _queryAgentWorkerPath;
    private readonly string _lintAgentWorkerPath;

    /// <summary>
    /// <paramref name="ingestAgentWorkerPath"/>, <paramref name="queryAgentWorkerPath"/> and
    /// <paramref name="lintAgentWorkerPath"/> are the Hub-resolved
    /// <c>&lt;AgentDir&gt;/&lt;agent-id&gt;/Grimoire.&lt;Type&gt;Agent.dll</c> worker
    /// locations (ADR-022 — no longer independently configurable; all three are governed
    /// entirely by <c>--agent-dir</c>). <c>GrimoirePathResolver</c> validates all
    /// three exist before the host is ever constructed, so every spawn here launches
    /// exactly one way: <c>dotnet &lt;dll&gt;</c> — the hub consumes build artifacts and
    /// never produces them (rule R4, no <c>.csproj</c>/bare-executable launch mode exists
    /// any more).
    /// </summary>
    public AgentProcessHost(
        LocalSecretsLoader secretsLoader,
        string ingestAgentWorkerPath,
        string queryAgentWorkerPath,
        string lintAgentWorkerPath)
    {
        _secretsLoader = secretsLoader;
        _ingestAgentWorkerPath = ingestAgentWorkerPath;
        _queryAgentWorkerPath = queryAgentWorkerPath;
        _lintAgentWorkerPath = lintAgentWorkerPath;
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

    /// <summary>ADR-011: spawns a Query agent process (see the interface doc for port-reuse rationale).</summary>
    public async Task<IAgentProcessHandle> StartAsync(QueryAgentRequest request, CancellationToken cancellationToken = default)
    {
        var process = StartQueryProcess(request);

        // Conversation input (prompt + prior turns) goes on stdin as JSON, unlike
        // Ingest's plain pasted-text stdin — mirrors the convention, not the payload
        // shape (QueryConversationInput in Grimoire.QueryAgent).
        var stdinPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            prompt = request.Prompt,
            priorTurns = request.PriorTurns.Select(t => new
            {
                position = t.Position,
                prompt = t.Prompt,
                answer = t.Answer,
                state = t.State,
            }),
        });
        await process.StandardInput.WriteAsync(stdinPayload);
        process.StandardInput.Close();

        return new ProcessHandle(process);
    }

    /// <summary>ADR-016 (013-lint-agent): spawns a Lint agent process. No stdin payload at
    /// all — Lint takes no per-run input beyond the wiki itself (research.md).</summary>
    public Task<IAgentProcessHandle> StartAsync(LintAgentRequest request, CancellationToken cancellationToken = default)
    {
        var process = StartLintProcess(request);
        process.StandardInput.Close();
        return Task.FromResult<IAgentProcessHandle>(new ProcessHandle(process));
    }

    /// <summary>
    /// ADR-018 (015-lint-board-parity T032/T035): spawns the Lint agent binary in its
    /// remediation-execution invocation mode, reusing the Lint worker path and
    /// credential/env scoping unchanged. `Grimoire.LintAgent`'s own CLI (T035) parses
    /// `--mode remediation-execution` and everything below into a
    /// <c>RemediationExecutionCliOptions</c>, re-verifies the proposal (FR-018,
    /// judgment in agents/lint/system-prompt.md, T036), and either applies the fix or
    /// reports it moot. No stdin payload, matching Lint's own convention.
    /// </summary>
    public Task<IAgentProcessHandle> StartAsync(RemediationExecutionAgentRequest request, CancellationToken cancellationToken = default)
    {
        var process = StartRemediationProcess(request);
        process.StandardInput.Close();
        return Task.FromResult<IAgentProcessHandle>(new ProcessHandle(process));
    }

    /// <summary>
    /// ADR-018 (015-lint-board-parity T042): spawns the Lint agent binary in its
    /// message-turn invocation mode. Identity/policy/proposal arguments mirror
    /// <see cref="StartRemediationProcess"/> exactly (same worker binary, same Lint
    /// credential/env scoping); the new human message and prior-turn context — arbitrarily
    /// sized, unlike the fixed CLI identity args — travel on stdin as JSON, mirroring
    /// <see cref="StartAsync(QueryDispatch.QueryAgentRequest, CancellationToken)"/>'s
    /// prompt/priorTurns payload (both are the ADR-011 Query-turn shape).
    /// </summary>
    public async Task<IAgentProcessHandle> StartAsync(RemediationMessageTurnAgentRequest request, CancellationToken cancellationToken = default)
    {
        var process = StartMessageTurnProcess(request);

        var stdinPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            message = request.Message,
            priorMessages = request.PriorMessages.Select(m => new
            {
                sender = m.Sender,
                text = m.Text,
            }),
        });
        await process.StandardInput.WriteAsync(stdinPayload);
        process.StandardInput.Close();

        return new ProcessHandle(process);
    }

    private Process StartMessageTurnProcess(RemediationMessageTurnAgentRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.FileName = "dotnet";
        startInfo.ArgumentList.Add(_lintAgentWorkerPath);

        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add("message-turn");
        startInfo.ArgumentList.Add("--task-id");
        startInfo.ArgumentList.Add(request.TaskId);
        startInfo.ArgumentList.Add("--run-id");
        startInfo.ArgumentList.Add(request.RunId);
        startInfo.ArgumentList.Add("--wiki-root");
        startInfo.ArgumentList.Add(request.WikiRoot);
        startInfo.ArgumentList.Add("--system-prompt-path");
        startInfo.ArgumentList.Add(request.SystemPromptPath);
        startInfo.ArgumentList.Add("--policy-path");
        startInfo.ArgumentList.Add(request.PolicyPath);
        startInfo.ArgumentList.Add("--write-locks-dir");
        startInfo.ArgumentList.Add(request.WriteLocksDir);
        startInfo.ArgumentList.Add("--proposal-title");
        startInfo.ArgumentList.Add(request.Title);
        startInfo.ArgumentList.Add("--proposal-description");
        startInfo.ArgumentList.Add(request.Description);
        if (!string.IsNullOrWhiteSpace(request.TargetPath))
        {
            startInfo.ArgumentList.Add("--proposal-target-path");
            startInfo.ArgumentList.Add(request.TargetPath);
        }

        if (!string.IsNullOrWhiteSpace(request.AttachedContext))
        {
            startInfo.ArgumentList.Add("--attached-context");
            startInfo.ArgumentList.Add(request.AttachedContext);
        }

        var authToken = _secretsLoader.GetAnthropicAuthToken();
        var lintModel = _secretsLoader.GetLintModel();
        var lintBaseUrl = _secretsLoader.GetLintBase();
        var lintMaxOutputTokens = _secretsLoader.GetLintMaxOutputTokens();

        var baseEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in startInfo.Environment)
        {
            if (value is not null)
                baseEnv[key] = value;
        }

        var childEnv = BuildLintChildEnvironment(
            baseEnv, authToken, lintBaseUrl, lintModel, lintMaxOutputTokens, Activity.Current);
        startInfo.Environment.Clear();
        foreach (var (key, value) in childEnv)
        {
            startInfo.Environment[key] = value;
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start message-turn agent process.");
    }

    private Process StartRemediationProcess(RemediationExecutionAgentRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.FileName = "dotnet";
        startInfo.ArgumentList.Add(_lintAgentWorkerPath);

        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add("remediation-execution");
        startInfo.ArgumentList.Add("--task-id");
        startInfo.ArgumentList.Add(request.TaskId);
        startInfo.ArgumentList.Add("--run-id");
        startInfo.ArgumentList.Add(request.RunId);
        startInfo.ArgumentList.Add("--wiki-root");
        startInfo.ArgumentList.Add(request.WikiRoot);
        startInfo.ArgumentList.Add("--system-prompt-path");
        startInfo.ArgumentList.Add(request.SystemPromptPath);
        startInfo.ArgumentList.Add("--policy-path");
        startInfo.ArgumentList.Add(request.PolicyPath);
        startInfo.ArgumentList.Add("--write-locks-dir");
        startInfo.ArgumentList.Add(request.WriteLocksDir);
        startInfo.ArgumentList.Add("--proposal-title");
        startInfo.ArgumentList.Add(request.Title);
        startInfo.ArgumentList.Add("--proposal-description");
        startInfo.ArgumentList.Add(request.Description);
        if (!string.IsNullOrWhiteSpace(request.TargetPath))
        {
            startInfo.ArgumentList.Add("--proposal-target-path");
            startInfo.ArgumentList.Add(request.TargetPath);
        }

        // T035: US5's not-yet-built attach-context endpoint is the only future writer of
        // this field — always null today, so the argument is simply omitted.
        if (!string.IsNullOrWhiteSpace(request.AttachedContext))
        {
            startInfo.ArgumentList.Add("--attached-context");
            startInfo.ArgumentList.Add(request.AttachedContext);
        }

        var authToken = _secretsLoader.GetAnthropicAuthToken();
        var lintModel = _secretsLoader.GetLintModel();
        var lintBaseUrl = _secretsLoader.GetLintBase();
        var lintMaxOutputTokens = _secretsLoader.GetLintMaxOutputTokens();

        var baseEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in startInfo.Environment)
        {
            if (value is not null)
                baseEnv[key] = value;
        }

        var childEnv = BuildLintChildEnvironment(
            baseEnv, authToken, lintBaseUrl, lintModel, lintMaxOutputTokens, Activity.Current);
        startInfo.Environment.Clear();
        foreach (var (key, value) in childEnv)
        {
            startInfo.Environment[key] = value;
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start remediation-execution agent process.");
    }

    private Process StartLintProcess(LintAgentRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.FileName = "dotnet";
        startInfo.ArgumentList.Add(_lintAgentWorkerPath);

        startInfo.ArgumentList.Add("--run-id");
        startInfo.ArgumentList.Add(request.RunId);
        startInfo.ArgumentList.Add("--wiki-root");
        startInfo.ArgumentList.Add(request.WikiRoot);
        startInfo.ArgumentList.Add("--system-prompt-path");
        startInfo.ArgumentList.Add(request.SystemPromptPath);
        startInfo.ArgumentList.Add("--policy-path");
        startInfo.ArgumentList.Add(request.PolicyPath);
        startInfo.ArgumentList.Add("--write-locks-dir");
        startInfo.ArgumentList.Add(request.WriteLocksDir);
        startInfo.ArgumentList.Add("--review-window-days");
        startInfo.ArgumentList.Add(request.ReviewWindowDays.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var authToken = _secretsLoader.GetAnthropicAuthToken();
        var lintModel = _secretsLoader.GetLintModel();
        var lintBaseUrl = _secretsLoader.GetLintBase();
        var lintMaxOutputTokens = _secretsLoader.GetLintMaxOutputTokens();

        var baseEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in startInfo.Environment)
        {
            if (value is not null)
                baseEnv[key] = value;
        }

        var childEnv = BuildLintChildEnvironment(
            baseEnv, authToken, lintBaseUrl, lintModel, lintMaxOutputTokens, Activity.Current);
        startInfo.Environment.Clear();
        foreach (var (key, value) in childEnv)
        {
            startInfo.Environment[key] = value;
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start lint agent process.");
    }

    /// <summary>
    /// Lint's own credential/model-scoping (ADR-004) and trace-propagation (Constitution
    /// IV) env-var build — parallels <see cref="BuildQueryChildEnvironment"/> but with
    /// <c>GRIMOIRE_LINT_*</c> names so Lint's env stays independent of Ingest's/Query's,
    /// even though all three read the same <c>ANTHROPIC_AUTH_TOKEN</c> secret.
    /// </summary>
    private static Dictionary<string, string> BuildLintChildEnvironment(
        IDictionary<string, string> baseEnv,
        string? authToken,
        string? lintBaseUrl,
        string? lintModel,
        string? lintMaxOutputTokens,
        Activity? currentActivity)
    {
        var env = new Dictionary<string, string>(baseEnv, StringComparer.OrdinalIgnoreCase);
        env.Remove("ANTHROPIC_API_KEY");
        env.Remove("ANTHROPIC_AUTH_TOKEN");
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            env["ANTHROPIC_AUTH_TOKEN"] = authToken;
        }

        env.Remove("GRIMOIRE_LINT_MODEL");
        if (!string.IsNullOrWhiteSpace(lintModel))
        {
            env["GRIMOIRE_LINT_MODEL"] = lintModel;
        }

        env.Remove("GRIMOIRE_LINT_BASE_URL");
        if (!string.IsNullOrWhiteSpace(lintBaseUrl))
        {
            env["GRIMOIRE_LINT_BASE_URL"] = lintBaseUrl;
        }

        ApplyOptionalOverride(env, "GRIMOIRE_LINT_MAX_OUTPUT_TOKENS", lintMaxOutputTokens);

        env.Remove("TRACEPARENT");
        env.Remove("TRACESTATE");
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

    private Process StartQueryProcess(QueryAgentRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.FileName = "dotnet";
        startInfo.ArgumentList.Add(_queryAgentWorkerPath);

        startInfo.ArgumentList.Add("--turn-id");
        startInfo.ArgumentList.Add(request.TurnId);
        startInfo.ArgumentList.Add("--wiki-root");
        startInfo.ArgumentList.Add(request.WikiRoot);
        startInfo.ArgumentList.Add("--content-root");
        startInfo.ArgumentList.Add(request.ContentRoot);
        startInfo.ArgumentList.Add("--index-path");
        startInfo.ArgumentList.Add(request.IndexPath);
        startInfo.ArgumentList.Add("--log-path");
        startInfo.ArgumentList.Add(request.LogPath);
        startInfo.ArgumentList.Add("--system-prompt-path");
        startInfo.ArgumentList.Add(request.SystemPromptPath);
        startInfo.ArgumentList.Add("--policy-path");
        startInfo.ArgumentList.Add(request.PolicyPath);
        startInfo.ArgumentList.Add("--write-locks-dir");
        startInfo.ArgumentList.Add(request.WriteLocksDir);

        var authToken = _secretsLoader.GetAnthropicAuthToken();
        var queryModel = _secretsLoader.GetQueryModel();
        var queryBaseUrl = _secretsLoader.GetQueryBase();
        var queryMaxOutputTokens = _secretsLoader.GetQueryMaxOutputTokens();

        var baseEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in startInfo.Environment)
        {
            if (value is not null)
                baseEnv[key] = value;
        }

        var childEnv = BuildQueryChildEnvironment(
            baseEnv, authToken, queryBaseUrl, queryModel, queryMaxOutputTokens, Activity.Current);
        startInfo.Environment.Clear();
        foreach (var (key, value) in childEnv)
        {
            startInfo.Environment[key] = value;
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start query agent process.");
    }

    /// <summary>
    /// Query's own credential/model-scoping (ADR-004) and trace-propagation (Constitution
    /// IV) env-var build — parallels <see cref="BuildChildEnvironment"/> but with
    /// <c>GRIMOIRE_QUERY_*</c> names so Query's env stays independent of Ingest's, even
    /// though both read the same <c>ANTHROPIC_AUTH_TOKEN</c> secret.
    /// </summary>
    private static Dictionary<string, string> BuildQueryChildEnvironment(
        IDictionary<string, string> baseEnv,
        string? authToken,
        string? queryBaseUrl,
        string? queryModel,
        string? queryMaxOutputTokens,
        Activity? currentActivity)
    {
        var env = new Dictionary<string, string>(baseEnv, StringComparer.OrdinalIgnoreCase);
        env.Remove("ANTHROPIC_API_KEY");
        env.Remove("ANTHROPIC_AUTH_TOKEN");
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            env["ANTHROPIC_AUTH_TOKEN"] = authToken;
        }

        env.Remove("GRIMOIRE_QUERY_MODEL");
        if (!string.IsNullOrWhiteSpace(queryModel))
        {
            env["GRIMOIRE_QUERY_MODEL"] = queryModel;
        }

        env.Remove("GRIMOIRE_QUERY_BASE_URL");
        if (!string.IsNullOrWhiteSpace(queryBaseUrl))
        {
            env["GRIMOIRE_QUERY_BASE_URL"] = queryBaseUrl;
        }

        ApplyOptionalOverride(env, "GRIMOIRE_QUERY_MAX_OUTPUT_TOKENS", queryMaxOutputTokens);

        env.Remove("TRACEPARENT");
        env.Remove("TRACESTATE");
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

    private Process StartProcess(IngestAgentRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.FileName = "dotnet";
        startInfo.ArgumentList.Add(_ingestAgentWorkerPath);

        startInfo.ArgumentList.Add("--task-id");
        startInfo.ArgumentList.Add(request.TaskId);
        startInfo.ArgumentList.Add("--source-ref");
        startInfo.ArgumentList.Add(request.SourceRef);
        startInfo.ArgumentList.Add("--source-kind");
        startInfo.ArgumentList.Add(request.SourceKind);
        startInfo.ArgumentList.Add("--wiki-root");
        startInfo.ArgumentList.Add(request.WikiRoot);
        startInfo.ArgumentList.Add("--content-root");
        startInfo.ArgumentList.Add(request.ContentRoot);
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
        startInfo.ArgumentList.Add("--write-locks-dir");
        startInfo.ArgumentList.Add(request.WriteLocksDir);
        if (!string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            startInfo.ArgumentList.Add("--user-prompt");
            startInfo.ArgumentList.Add(request.UserPrompt);
        }
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            startInfo.ArgumentList.Add("--title");
            startInfo.ArgumentList.Add(request.Title);
        }

        var authToken = _secretsLoader.GetAnthropicAuthToken();
        var ingestModel = _secretsLoader.GetIngestModel();
        var ingestBaseUrl = _secretsLoader.GetIngestBase();
        var ingestTokenCap = _secretsLoader.GetIngestTokenCap();
        var ingestMaxOutputTokens = _secretsLoader.GetIngestMaxOutputTokens();
        // Build the child env by stripping credential keys from the parent env copy and
        // re-injecting only what was explicitly loaded from the secrets file (ADR-004).
        // Convert ProcessStartInfo.Environment (nullable values) to a non-nullable dict first.
        var baseEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in startInfo.Environment)
        {
            if (value is not null)
                baseEnv[key] = value;
        }

        var childEnv = BuildChildEnvironment(
            baseEnv, authToken, ingestBaseUrl, ingestModel, ingestTokenCap, ingestMaxOutputTokens,
            Activity.Current);
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
    /// <summary>
    /// #122: sets an optional per-agent override, or removes it outright when the operator
    /// configured none — so a variable left over in the Hub's own environment cannot leak
    /// into an agent whose <c>.env</c> says nothing about it, the same way the model and
    /// base-url variables are scrubbed before being re-injected (ADR-004).
    /// </summary>
    private static void ApplyOptionalOverride(
        IDictionary<string, string> env, string name, string? value)
    {
        env.Remove(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            env[name] = value;
        }
    }

    /// Exposed internally so tests can assert both guarantees without spawning a real process.
    /// </summary>
    public static Dictionary<string, string> BuildChildEnvironment(
        IDictionary<string, string> baseEnv,
        string? authToken,
        string? ingestBaseUrl = null,
        string? ingestModel = null,
        string? ingestTokenCap = null,
        string? ingestMaxOutputTokens = null,
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
            Console.WriteLine($"Using GRIMOIRE_INGEST_MODEL={effectiveModel} for Ingest agent process.");
            env["GRIMOIRE_INGEST_MODEL"] = effectiveModel;
        }

        var effectiveBaseUrl = !string.IsNullOrWhiteSpace(ingestBaseUrl)
            ? ingestBaseUrl
            : (baseEnv.TryGetValue("GRIMOIRE_INGEST_BASE_URL", out var inheritedBaseUrl) ? inheritedBaseUrl : null);
        env.Remove("GRIMOIRE_INGEST_BASE_URL");
        if (!string.IsNullOrWhiteSpace(effectiveBaseUrl))
        {
            Console.WriteLine($"Using GRIMOIRE_INGEST_BASE_URL={effectiveBaseUrl} for Ingest agent process.");
            env["GRIMOIRE_INGEST_BASE_URL"] = effectiveBaseUrl;
        }

        var effectiveTokenCap = !string.IsNullOrWhiteSpace(ingestTokenCap)
            ? ingestTokenCap
            : (baseEnv.TryGetValue("GRIMOIRE_INGEST_TOKEN_CAP", out var inheritedTokenCap) ? inheritedTokenCap : null);
        env.Remove("GRIMOIRE_INGEST_TOKEN_CAP");
        if (!string.IsNullOrWhiteSpace(effectiveTokenCap))
        {
            env["GRIMOIRE_INGEST_TOKEN_CAP"] = effectiveTokenCap;
        }

        var effectiveMaxOutputTokens = !string.IsNullOrWhiteSpace(ingestMaxOutputTokens)
            ? ingestMaxOutputTokens
            : (baseEnv.TryGetValue("GRIMOIRE_INGEST_MAX_OUTPUT_TOKENS", out var inheritedMax) ? inheritedMax : null);
        ApplyOptionalOverride(env, "GRIMOIRE_INGEST_MAX_OUTPUT_TOKENS", effectiveMaxOutputTokens);

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
                catch (ObjectDisposedException)
                {
                    // The handle was disposed (e.g. right after a terminal event) while this
                    // background drain was still in flight; nothing left to read.
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
