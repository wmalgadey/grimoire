using System.Diagnostics;
using System.Text.Json;
using Grimoire.EvalRunner.Providers;

namespace Grimoire.EvalRunner.Workspace;

/// <summary>
/// One agent-proposed remediation action as reported on the terminal event's
/// `proposedActions` field (015-lint-board-parity T028, contracts/
/// remediation-lifecycle-events.md). Eval-runner-local mirror of
/// `Grimoire.Hub.AgentDispatch.AgentRunEventProposedAction` — this assembly does not
/// reference `Grimoire.Hub` (it spawns agent processes and reads their stdout directly),
/// so the shape is duplicated here rather than shared.
/// </summary>
public sealed record RemediationProposalEntry(string Title, string Description, string? TargetPath);

/// <summary>
/// One spawned Lint agent run's outcome. Like Query (<see cref="QueryAgentRunResult"/>),
/// Lint writes no Task-Artifact-equivalent of its own — the outcome is read entirely from
/// the NDJSON `completed`/`failed` terminal event on stdout (`RunEventEmitter` contract).
/// Unlike Query, a Lint run has no per-run prompt, no prior turns, and no created pages
/// (its one write rule is the frontmatter-only Inbound-Link Refresh, mechanically
/// re-derivable from the post-run wiki state alone — <see cref="LintSampleRunData"/>'s
/// reason for carrying <c>WikiRoot</c> instead of a created-paths list).
/// <see cref="ProposedActions"/> (015-lint-board-parity T028) is the terminal event's
/// `proposedActions` field, parsed entry-tolerantly like the Hub's own
/// `TolerantProposedActionListConverter` — null/empty when the run proposed nothing.
/// </summary>
public sealed record LintAgentRunResult(
    int ExitCode,
    bool TimedOut,
    string StdErr,
    bool Completed,
    string? Narrative,
    string? FailureReason,
    IReadOnlyList<RemediationProposalEntry>? ProposedActions = null);

/// <summary>
/// One spawned remediation-execution run's outcome (T039, 015-lint-board-parity,
/// FR-018) — mirrors <see cref="LintAgentRunResult"/> for the sibling invocation mode:
/// read entirely from the NDJSON terminal event, no task-artifact-equivalent of its own.
/// <see cref="RemediationOutcome"/>/<see cref="Reason"/> are the re-verification verdict
/// (contracts/remediation-lifecycle-events.md `remediationOutcome`) — transported here
/// exactly as the Hub transports it (Constitution Principle V: this eval-runner assembly
/// never computes the verdict, only reads what the agent reported).
/// </summary>
public sealed record RemediationExecutionRunResult(
    int ExitCode,
    bool TimedOut,
    string StdErr,
    bool Completed,
    string? Narrative,
    string? FailureReason,
    string? RemediationOutcome,
    string? Reason);

/// <summary>
/// Spawns the real <c>Grimoire.LintAgent</c> executable per sample through its production
/// CLI contract (ADR-013), mirroring <see cref="QueryAgentProcessInvoker"/>'s relationship
/// to Grimoire.QueryAgent. Deferred from T017 (013-lint-agent) to this Phase 6 capture
/// task (T046) — see that task's deviation note. The only <see cref="Process"/> user for
/// Lint in this assembly (ADR-012 C8).
/// </summary>
public sealed class LintAgentProcessInvoker
{
    private static readonly string[] ScrubbedVariables =
    [
        "ANTHROPIC_AUTH_TOKEN",
        "GRIMOIRE_LINT_BASE_URL",
        "GRIMOIRE_LINT_MODEL",
        "GRIMOIRE_EVAL_PROVIDER_BASE_URL",
        "GRIMOIRE_EVAL_PROVIDER_MODEL",
        "GRIMOIRE_EVAL_PROVIDER_API_KEY",
        "GRIMOIRE_MODEL_REPLAY_PATH",
        "GRIMOIRE_MODEL_CAPTURE_PATH",
        // No OTLP export from spawned eval children — their telemetry is not production data.
        "OTEL_EXPORTER_OTLP_ENDPOINT",
    ];

    private readonly string _agentDllPath;

    public LintAgentProcessInvoker(string agentDllPath)
    {
        _agentDllPath = agentDllPath;
    }

    public static LintAgentProcessInvoker ForRepo(EvalPaths paths)
        => new(ResolveAgentDllPath(paths.RepoRoot));

    /// <summary>Mirrors <see cref="AgentProcessInvoker.ResolveAgentDllPath"/> for Lint's own build output.</summary>
    public static string ResolveAgentDllPath(string repoRoot)
    {
        var separator = Path.DirectorySeparatorChar;
        var preferred = AppContext.BaseDirectory.Contains($"{separator}Release{separator}", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Release", "Debug" }
            : ["Debug", "Release"];

        foreach (var configuration in preferred)
        {
            var candidate = Path.Combine(
                repoRoot, "backend", "src", "Grimoire.LintAgent", "bin", configuration, "net10.0", "Grimoire.LintAgent.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Grimoire.LintAgent.dll not found in its build output. Build first: dotnet build backend/Grimoire.slnx");
    }

    public async Task<LintAgentRunResult> RunAsync(
        string runId,
        string wikiRoot,
        EvalPaths paths,
        AgentModelMode mode,
        TimeSpan budget,
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
        AddOption(startInfo, "--run-id", runId);
        AddOption(startInfo, "--wiki-root", wikiRoot);
        AddOption(startInfo, "--system-prompt-path", paths.LintSystemPromptPath);
        AddOption(startInfo, "--policy-path", paths.LintPolicyPath);
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
                    startInfo.Environment["GRIMOIRE_LINT_BASE_URL"] = mode.Provider.BaseUrl!;
                    startInfo.Environment["GRIMOIRE_LINT_MODEL"] = mode.Provider.Model!;
                    startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] =
                        Environment.GetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY") ?? string.Empty;
                    break;
                case ProviderKind.Anthropic:
                    startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] =
                        Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN") ?? string.Empty;
                    if (mode.Provider.Model is not null)
                    {
                        startInfo.Environment["GRIMOIRE_LINT_MODEL"] = mode.Provider.Model;
                    }

                    break;
                default:
                    throw new InvalidOperationException("Capture mode requires a resolved provider configuration.");
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start the lint agent process ({_agentDllPath}).");

        // Lint takes no stdin payload at all (no pasted source, no prompt) — close stdin
        // immediately so the child never blocks waiting for input it will never receive.
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

            return new LintAgentRunResult(
                ExitCode: -1, TimedOut: true, StdErr: SafeResult(stdErrTask),
                Completed: false, Narrative: null, FailureReason: null);
        }

        var stdout = SafeResult(stdOutTask);
        var stderr = SafeResult(stdErrTask);
        var (completed, narrative, failureReason, proposedActions) = ParseTerminalEvent(stdout);

        return new LintAgentRunResult(
            process.ExitCode, TimedOut: false, StdErr: stderr, completed, narrative, failureReason, proposedActions);
    }

    /// <summary>
    /// T039 (015-lint-board-parity, FR-018): spawns the Lint agent binary in its
    /// remediation-execution invocation mode
    /// (<c>backend/src/Grimoire.LintAgent/RemediationExecutionCliOptions.cs</c>'s CLI
    /// contract), replaying or capturing exactly like <see cref="RunAsync"/> — same
    /// process-management shape, only the argument list and terminal-event fields differ.
    /// </summary>
    public async Task<RemediationExecutionRunResult> RunRemediationExecutionAsync(
        string taskId,
        string runId,
        string wikiRoot,
        EvalPaths paths,
        AgentModelMode mode,
        TimeSpan budget,
        string writeLocksDir,
        string proposalTitle,
        string proposalDescription,
        string? proposalTargetPath,
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
        AddOption(startInfo, "--mode", "remediation-execution");
        AddOption(startInfo, "--task-id", taskId);
        AddOption(startInfo, "--run-id", runId);
        AddOption(startInfo, "--wiki-root", wikiRoot);
        AddOption(startInfo, "--system-prompt-path", paths.LintSystemPromptPath);
        AddOption(startInfo, "--policy-path", paths.LintPolicyPath);
        AddOption(startInfo, "--write-locks-dir", writeLocksDir);
        AddOption(startInfo, "--proposal-title", proposalTitle);
        AddOption(startInfo, "--proposal-description", proposalDescription);
        if (!string.IsNullOrWhiteSpace(proposalTargetPath))
        {
            AddOption(startInfo, "--proposal-target-path", proposalTargetPath);
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
                    startInfo.Environment["GRIMOIRE_LINT_BASE_URL"] = mode.Provider.BaseUrl!;
                    startInfo.Environment["GRIMOIRE_LINT_MODEL"] = mode.Provider.Model!;
                    startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] =
                        Environment.GetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY") ?? string.Empty;
                    break;
                case ProviderKind.Anthropic:
                    startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] =
                        Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN") ?? string.Empty;
                    if (mode.Provider.Model is not null)
                    {
                        startInfo.Environment["GRIMOIRE_LINT_MODEL"] = mode.Provider.Model;
                    }

                    break;
                default:
                    throw new InvalidOperationException("Capture mode requires a resolved provider configuration.");
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start the remediation-execution agent process ({_agentDllPath}).");

        // No stdin payload at all — matches Lint's own convention (the proposal already
        // rides the CLI args, mirroring AgentProcessHost.StartRemediationProcess).
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

            return new RemediationExecutionRunResult(
                ExitCode: -1, TimedOut: true, StdErr: SafeResult(stdErrTask),
                Completed: false, Narrative: null, FailureReason: null, RemediationOutcome: null, Reason: null);
        }

        var stdout = SafeResult(stdOutTask);
        var stderr = SafeResult(stdErrTask);
        var (completed, narrative, failureReason, remediationOutcome, reason) = ParseRemediationTerminalEvent(stdout);

        return new RemediationExecutionRunResult(
            process.ExitCode, TimedOut: false, StdErr: stderr, completed, narrative, failureReason,
            remediationOutcome, reason);
    }

    /// <summary>
    /// Scans the NDJSON stdout stream for the remediation-execution run's one terminal
    /// event and extracts the narrative, the failure reason, and the re-verification
    /// verdict (<c>remediationOutcome</c>/<c>reason</c>, contracts/
    /// remediation-lifecycle-events.md). Mirrors <see cref="ParseTerminalEvent"/>.
    /// </summary>
    private static (bool Completed, string? Narrative, string? FailureReason, string? RemediationOutcome, string? Reason)
        ParseRemediationTerminalEvent(string stdout)
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

            var remediationOutcome = root.TryGetProperty("remediationOutcome", out var outcomeProperty)
                && outcomeProperty.ValueKind == JsonValueKind.String
                ? outcomeProperty.GetString()
                : null;

            var completed = type == "completed";
            return (completed, summary, completed ? null : reason, remediationOutcome, reason);
        }

        return (false, null, null, null, null);
    }

    /// <summary>
    /// Scans the NDJSON stdout stream for the run's one terminal event (`completed` or
    /// `failed`, `RunEventEmitter.EmitCompleted`/`EmitFailed`) and extracts the narrative
    /// (`summary` — the Findings Report body), the failure reason (`reason`), and
    /// (015-lint-board-parity T028) the `proposedActions` list. Mirrors
    /// <see cref="QueryAgentProcessInvoker"/>'s parser; Lint has no denied actions or
    /// created-pages field worth surfacing here — the write-scope guarantee is covered by
    /// its own dedicated integration tests (T039-T041), and the Inbound-Link Refresh's
    /// post-run truth is recomputed from <c>wikiRoot</c> directly by the scorer, never
    /// from this event.
    /// </summary>
    private static (bool Completed, string? Narrative, string? FailureReason, IReadOnlyList<RemediationProposalEntry>? ProposedActions)
        ParseTerminalEvent(string stdout)
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

            var proposedActions = ParseProposedActions(root);

            return (type == "completed", summary, reason, proposedActions);
        }

        return (false, null, null, null);
    }

    /// <summary>
    /// Entry-tolerant `proposedActions` reader (mirrors the Hub's
    /// `TolerantProposedActionListConverter`): a malformed entry — not an object, missing
    /// or non-string `title`/`description` — is skipped rather than failing the whole
    /// parse; a value that is not an array (including absent) yields null.
    /// </summary>
    private static IReadOnlyList<RemediationProposalEntry>? ParseProposedActions(JsonElement root)
    {
        if (!root.TryGetProperty("proposedActions", out var proposedActionsProperty)
            || proposedActionsProperty.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var actions = new List<RemediationProposalEntry>();
        foreach (var element in proposedActionsProperty.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = TryGetNonEmptyString(element, "title");
            var description = TryGetNonEmptyString(element, "description");
            if (title is null || description is null)
            {
                continue;
            }

            actions.Add(new RemediationProposalEntry(title, description, TryGetNonEmptyString(element, "targetPath")));
        }

        return actions;
    }

    private static string? TryGetNonEmptyString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && property.GetString() is { } value
            && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

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
