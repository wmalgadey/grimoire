using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Instructions;
using Grimoire.IngestAgent;
using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent.IngestLog;
using Grimoire.IngestAgent.TaskArtifact;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentEvals;

public sealed class EvalFactAttribute : FactAttribute
{
    public EvalFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GRIMOIRE_EVAL"), "1", StringComparison.Ordinal))
        {
            Skip = "Set GRIMOIRE_EVAL=1 and either ANTHROPIC_AUTH_TOKEN or the GRIMOIRE_EVAL_PROVIDER_* " +
                "variables to run agent-behavior evals.";
            return;
        }

        var outcome = EvalProviderResolver.Resolve();
        switch (outcome.Status)
        {
            case EvalGateStatus.Enabled:
                break;
            case EvalGateStatus.Skipped:
                Skip = outcome.Reason;
                break;
            case EvalGateStatus.ConfigurationError:
                // FR-012: fail loudly rather than skip when the configuration is ambiguous.
                throw new InvalidOperationException(outcome.Reason);
            default:
                throw new InvalidOperationException($"Unhandled eval gate status: {outcome.Status}.");
        }
    }
}

public static class EvalGate
{
    public static int ResolveSampleCount()
    {
        var raw = Environment.GetEnvironmentVariable("GRIMOIRE_EVAL_SAMPLES");
        if (!int.TryParse(raw, out var value))
        {
            return 10;
        }

        return Math.Clamp(value, 1, 20);
    }
}

/// <summary>Which provider (if any) is active for an eval run (data-model.md#ProviderKind).</summary>
public enum ProviderKind
{
    None,
    Anthropic,
    Affordable,
}

/// <summary>
/// The resolved, validated configuration for one eval run (data-model.md#ProviderConfiguration).
/// A configuration is "affordable-complete" only when all three of BaseUrl, Model, and the
/// credential are non-empty; a partial affordable configuration does not count as present.
/// </summary>
public sealed record ProviderConfiguration(ProviderKind Kind, string? BaseUrl, string? Model, bool HasCredential)
{
    public static readonly ProviderConfiguration None = new(ProviderKind.None, null, null, false);
}

/// <summary>Outcome status of resolving the eval provider gate (data-model.md#EvalGateOutcome).</summary>
public enum EvalGateStatus
{
    Enabled,
    Skipped,
    ConfigurationError,
}

/// <summary>
/// The result of resolving <see cref="ProviderConfiguration"/> (data-model.md#EvalGateOutcome).
/// <see cref="Reason"/> is null when <see cref="Status"/> is <see cref="EvalGateStatus.Enabled"/>.
/// </summary>
public sealed record EvalGateOutcome(EvalGateStatus Status, ProviderConfiguration Configuration, string? Reason);

/// <summary>
/// Resolves which model provider (if any) serves an eval run, as a pure function of
/// environment variables (contracts/eval-provider-env-vars.md). Reused both by
/// <see cref="EvalFactAttribute"/> (gate/skip decision) and <see cref="AgentEvalRunner"/>
/// (provider wiring).
/// </summary>
public static class EvalProviderResolver
{
    public const string NeitherConfiguredReason =
        "Set ANTHROPIC_AUTH_TOKEN, or all three of GRIMOIRE_EVAL_PROVIDER_BASE_URL/" +
        "GRIMOIRE_EVAL_PROVIDER_MODEL/GRIMOIRE_EVAL_PROVIDER_API_KEY, to run agent-behavior evals.";

    public const string BothConfiguredReason =
        "Both ANTHROPIC_AUTH_TOKEN and a complete GRIMOIRE_EVAL_PROVIDER_* configuration " +
        "(BASE_URL + MODEL + API_KEY) are set. Configure exactly one provider for agent evals.";

    public static EvalGateOutcome Resolve() => Resolve(Environment.GetEnvironmentVariable);

    internal static EvalGateOutcome Resolve(Func<string, string?> getEnvironmentVariable)
    {
        var anthropicToken = getEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        var anthropicPresent = !string.IsNullOrWhiteSpace(anthropicToken);

        var affordableBaseUrl = getEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_BASE_URL");
        var affordableModel = getEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_MODEL");
        var affordableApiKey = getEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY");
        var affordableComplete = !string.IsNullOrWhiteSpace(affordableBaseUrl)
            && !string.IsNullOrWhiteSpace(affordableModel)
            && !string.IsNullOrWhiteSpace(affordableApiKey);

        if (anthropicPresent && affordableComplete)
        {
            return new EvalGateOutcome(EvalGateStatus.ConfigurationError, ProviderConfiguration.None, BothConfiguredReason);
        }

        if (anthropicPresent)
        {
            var anthropicModel = getEnvironmentVariable("GRIMOIRE_INGEST_MODEL");
            var configuration = new ProviderConfiguration(ProviderKind.Anthropic, BaseUrl: null, Model: anthropicModel, HasCredential: true);
            return new EvalGateOutcome(EvalGateStatus.Enabled, configuration, Reason: null);
        }

        if (affordableComplete)
        {
            var configuration = new ProviderConfiguration(ProviderKind.Affordable, affordableBaseUrl, affordableModel, HasCredential: true);
            return new EvalGateOutcome(EvalGateStatus.Enabled, configuration, Reason: null);
        }

        return new EvalGateOutcome(EvalGateStatus.Skipped, ProviderConfiguration.None, NeitherConfiguredReason);
    }
}

/// <summary>
/// Decorates any <see cref="IModelClient"/> (ADR-010 port) with a call-timeout bound
/// (data-model.md#TimeoutEnforcingModelClient). Races the inner call against the timeout
/// rather than relying on the inner client to observe cancellation, so a hung call cannot
/// block an eval run past FR-013's bound.
/// </summary>
public sealed class TimeoutEnforcingModelClient : IModelClient
{
    private readonly IModelClient _inner;
    private readonly TimeSpan _timeout;

    public TimeoutEnforcingModelClient(IModelClient inner, TimeSpan? timeout = null)
    {
        _inner = inner;
        _timeout = timeout ?? TimeSpan.FromSeconds(120);
    }

    public string ModelId => _inner.ModelId;

    public async Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversation,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        var innerTask = _inner.NextTurnAsync(systemPrompt, conversation, tools, cancellationToken);
        var timeoutTask = Task.Delay(_timeout, cancellationToken);

        var completed = await Task.WhenAny(innerTask, timeoutTask);
        if (completed == timeoutTask)
        {
            throw new ModelCallTimeoutException(_timeout);
        }

        return await innerTask;
    }
}

/// <summary>
/// Thrown by <see cref="TimeoutEnforcingModelClient"/> when a single provider call exceeds
/// its bound (FR-013) — distinct from a connectivity failure or an agent-judgment failure.
/// </summary>
public sealed class ModelCallTimeoutException : Exception
{
    public ModelCallTimeoutException(TimeSpan timeout)
        : base($"Model call exceeded the {timeout.TotalSeconds:0}s timeout.")
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}

/// <summary>
/// Emits the eval-harness observability contract (plan.md ## Observability):
/// the `grimoire.eval.gate_resolutions_total` counter, the `eval_provider_resolved` and
/// `eval_sample_timeout` structured log events, and the `eval.gate_resolution` trace span.
/// </summary>
public static class EvalObservability
{
    public static readonly ActivitySource ActivitySource = new("Grimoire.AgentEvals", "1.0.0");

    private static readonly Meter Meter = new("Grimoire.AgentEvals", "1.0.0");

    private static readonly Counter<long> GateResolutionsTotal = Meter.CreateCounter<long>(
        "grimoire.eval.gate_resolutions_total",
        description: "Number of eval-suite provider gate resolutions, labeled by provider and outcome.");

    private static readonly EventId ProviderResolvedEvent = new(1, "eval_provider_resolved");
    private static readonly EventId SampleTimeoutEvent = new(2, "eval_sample_timeout");

    public static void RecordGateResolution(ILogger logger, EvalGateOutcome outcome)
    {
        var provider = ProviderLabel(outcome.Configuration.Kind);
        var outcomeLabel = OutcomeLabel(outcome.Status);
        var model = outcome.Configuration.Model;

        using var span = ActivitySource.StartActivity("eval.gate_resolution");
        span?.SetTag("provider", provider);
        span?.SetTag("outcome", outcomeLabel);
        span?.SetTag("model", model);

        GateResolutionsTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("outcome", outcomeLabel));

        logger.LogInformation(
            ProviderResolvedEvent,
            "Eval provider gate resolved. provider={provider} outcome={outcome} model={model} reason={reason}",
            provider,
            outcomeLabel,
            model,
            outcome.Reason);
    }

    public static void RecordSampleTimeout(ILogger logger, string evalName, string provider, string? model, double timeoutSeconds)
    {
        logger.LogWarning(
            SampleTimeoutEvent,
            "Eval sample exceeded the provider call timeout. eval_name={eval_name} provider={provider} model={model} timeout_seconds={timeout_seconds}",
            evalName,
            provider,
            model,
            timeoutSeconds);
    }

    public static string ProviderLabel(ProviderKind kind) => kind switch
    {
        ProviderKind.Anthropic => "anthropic",
        ProviderKind.Affordable => "affordable",
        _ => "none",
    };

    private static string OutcomeLabel(EvalGateStatus status) => status switch
    {
        EvalGateStatus.Enabled => "enabled",
        EvalGateStatus.Skipped => "skipped",
        EvalGateStatus.ConfigurationError => "configuration_error",
        _ => "unknown",
    };
}

public sealed record EvalRunResult(
    string TaskId,
    string Status,
    TaskArtifactDocument Artifact,
    string SandboxRoot,
    string TranscriptPath,
    IReadOnlyList<string> PageFiles,
    string IndexContent);

public sealed class AgentEvalRunner
{
    private readonly string _repoRoot;
    private readonly string _fixturesRoot;
    private readonly string _transcriptRoot;
    private readonly ILogger<AgentEvalRunner> _logger;

    public AgentEvalRunner(ILogger<AgentEvalRunner>? logger = null)
    {
        _repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        _fixturesRoot = Path.Combine(_repoRoot, "backend", "tests", "Grimoire.AgentEvals", "Fixtures");
        _transcriptRoot = Path.Combine(Path.GetTempPath(), "grimoire-agent-evals", "transcripts");
        _logger = logger ?? NullLogger<AgentEvalRunner>.Instance;
        Directory.CreateDirectory(_transcriptRoot);
    }

    /// <summary>
    /// Constructs an <see cref="AnthropicModelClient"/> wired to the given resolved provider
    /// (data-model.md#ProviderConfiguration), for any caller that needs its own client outside
    /// the main agent loop (e.g. an eval's LLM-judge client) — not just <see cref="RunAsync"/>.
    /// When affordable, the process env vars <c>AnthropicModelClient</c>'s constructor reads
    /// are set from the resolved configuration immediately before construction, then restored
    /// to their prior value immediately after: the constructor reads them once, synchronously,
    /// and caches what it needs, so this shim never needs to outlive the constructor call and
    /// never leaks into a later resolution within the same process (which would otherwise see
    /// its own prior <c>ANTHROPIC_AUTH_TOKEN</c> mutation alongside the still-set
    /// <c>GRIMOIRE_EVAL_PROVIDER_*</c> vars and misreport a both-configured conflict).
    /// </summary>
    internal static AnthropicModelClient CreateProviderWiredAnthropicClient(ProviderConfiguration configuration)
    {
        if (configuration.Kind != ProviderKind.Affordable)
        {
            return new AnthropicModelClient();
        }

        var originalIngestBaseUrl = Environment.GetEnvironmentVariable("GRIMOIRE_INGEST_BASE_URL");
        var originalIngestModel = Environment.GetEnvironmentVariable("GRIMOIRE_INGEST_MODEL");
        var originalAnthropicToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");

        Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_BASE_URL", configuration.BaseUrl);
        Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_MODEL", configuration.Model);
        Environment.SetEnvironmentVariable(
            "ANTHROPIC_AUTH_TOKEN",
            Environment.GetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY"));

        try
        {
            return new AnthropicModelClient();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_BASE_URL", originalIngestBaseUrl);
            Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_MODEL", originalIngestModel);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", originalAnthropicToken);
        }
    }

    public async Task<EvalRunResult> RunAsync(
        string fixtureName,
        string sourceContent,
        string runLabel,
        Action<string>? mutateSystemPrompt,
        CancellationToken cancellationToken,
        string? userPrompt = null)
    {
        var taskId = $"eval-{runLabel}-{Guid.NewGuid():N}";
        var sandboxRoot = Path.Combine(Path.GetTempPath(), "grimoire-agent-evals", taskId);
        var fixtureRoot = Path.Combine(_fixturesRoot, fixtureName);
        var wikiFixtureRoot = Path.Combine(fixtureRoot, "wiki");

        CopyDirectory(wikiFixtureRoot, Path.Combine(sandboxRoot, "wiki"));
        CopyDirectory(Path.Combine(_repoRoot, "data", "agents", "ingest"), Path.Combine(sandboxRoot, "agents", "ingest"));

        if (mutateSystemPrompt is not null)
        {
            var systemPromptPath = Path.Combine(sandboxRoot, "agents", "ingest", "system-prompt.md");
            mutateSystemPrompt(systemPromptPath);
        }

        var options = new AgentCliOptions(
            TaskId: taskId,
            SourceRef: $"eval://{fixtureName}/{runLabel}",
            SourceKind: "pasted_text",
            WikiRoot: Path.Combine(sandboxRoot, "wiki"),
            PagesDir: Path.Combine(sandboxRoot, "wiki", "pages"),
            TasksDir: Path.Combine(sandboxRoot, "wiki", "tasks"),
            IndexPath: Path.Combine(sandboxRoot, "wiki", "index.md"),
            LogPath: Path.Combine(sandboxRoot, "wiki", "log.md"),
            PastedText: sourceContent,
            SystemPromptPath: Path.Combine(sandboxRoot, "agents", "ingest", "system-prompt.md"),
            DefaultUserPromptPath: Path.Combine(sandboxRoot, "agents", "ingest", "default-user-prompt.md"),
            UserPrompt: userPrompt,
            PolicyPath: Path.Combine(sandboxRoot, "agents", "ingest", "policy.json"));

        Directory.CreateDirectory(options.PagesDir);
        Directory.CreateDirectory(options.TasksDir);

        var gateOutcome = EvalProviderResolver.Resolve();
        EvalObservability.RecordGateResolution(_logger, gateOutcome);
        if (gateOutcome.Status != EvalGateStatus.Enabled)
        {
            throw new InvalidOperationException(gateOutcome.Reason ?? "Eval provider is not configured.");
        }

        var configuration = gateOutcome.Configuration;

        var taskStore = new TaskArtifactStore();
        var logAppender = new IngestLogAppender();
        var startTime = DateTimeOffset.UtcNow;

        await taskStore.WriteAsync(
            options.TaskArtifactPath,
            new TaskArtifactDocument(
                TaskId: options.TaskId,
                Type: "ingest",
                Status: "running",
                Agent: "ingest",
                StartedAt: startTime,
                CompletedAt: null,
                SourceRef: options.SourceRef,
                PagesTouched: [],
                FailureReason: null,
                Narrative: "Eval run started."),
            cancellationToken);

        IModelClient anthropicModelClient = CreateProviderWiredAnthropicClient(configuration);

        var recordingModelClient = new RecordingModelClient(new TimeoutEnforcingModelClient(anthropicModelClient));
        var promptLoader = new SystemPromptLoader();
        var instructionResult = await promptLoader.LoadAsync(options.SystemPromptPath, cancellationToken);
        if (instructionResult.IsSecond(out var instructionFailure))
        {
            throw new InvalidOperationException($"System prompt invalid for eval run: {instructionFailure.Reason}");
        }

        instructionResult.IsFirst(out var systemPromptDoc);

        string effectiveUserPrompt;
        string userPromptSource;
        if (!string.IsNullOrWhiteSpace(options.UserPrompt))
        {
            effectiveUserPrompt = options.UserPrompt.Trim();
            userPromptSource = "custom";
        }
        else
        {
            var defaultPromptResult = await promptLoader.LoadAsync(options.DefaultUserPromptPath, cancellationToken);
            if (defaultPromptResult.IsSecond(out var defaultPromptFailure))
            {
                throw new InvalidOperationException($"Default user prompt invalid for eval run: {defaultPromptFailure.Reason}");
            }

            defaultPromptResult.IsFirst(out var loadedDefaultPrompt);
            effectiveUserPrompt = loadedDefaultPrompt!.Content.Trim();
            userPromptSource = "default";
        }

        var policyLoader = new PolicyLoader(sandboxRoot);
        var policyResult = await policyLoader.LoadAsync(options.PolicyPath, cancellationToken);
        if (policyResult.IsSecond(out var policyFailure))
        {
            throw new InvalidOperationException($"Policy invalid for eval run: {policyFailure.Reason}");
        }

        policyResult.IsFirst(out var loadedPolicy);

        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(loadedPolicy!.Policy, journal, sandboxRoot);
        var loop = new AgentLoop(recordingModelClient, executor);

        try
        {
            var loopResult = await loop.RunAsync(
                systemPromptDoc!.Content,
                effectiveUserPrompt,
                options.TaskId,
                options.SourceRef,
                options.PastedText ?? string.Empty,
                cancellationToken);

            var touched = executor.TouchedPaths.Select(p => Path.GetRelativePath(sandboxRoot, p)).ToList();
            var doc = new TaskArtifactDocument(
                TaskId: options.TaskId,
                Type: "ingest",
                Status: "completed",
                Agent: "ingest",
                StartedAt: startTime,
                CompletedAt: DateTimeOffset.UtcNow,
                SourceRef: options.SourceRef,
                PagesTouched: touched,
                FailureReason: null,
                Narrative: loopResult.Narrative,
                PagesCreated: touched,
                PagesUpdated: [],
                PagesSuperseded: [],
                DeniedActions: executor.Denials.Select(d => new DeniedActionEntry(d.Action, d.RequestedTarget, d.CanonicalTarget, d.Reason, d.Turn)).ToList(),
                InstructionFiles: [new InstructionFileRecord(systemPromptDoc.Path, systemPromptDoc.Sha256)],
                Policy: new PolicyRecord(loadedPolicy.Identity.Path, loadedPolicy.Identity.Version, loadedPolicy.Identity.Sha256),
                Model: recordingModelClient.ModelId,
                Turns: loopResult.TurnsUsed,
                RolledBack: null,
                UserPromptSource: userPromptSource,
                UserPrompt: effectiveUserPrompt);

            await taskStore.WriteAsync(options.TaskArtifactPath, doc, cancellationToken);
            await logAppender.EnsureLogEntryAsync(options.LogPath, "completed", options.SourceRef, options.TaskId, false, cancellationToken);

            var transcriptPath = await WriteTranscriptAsync(taskId, recordingModelClient, cancellationToken);
            var artifact = await taskStore.ReadAsync(options.TaskArtifactPath, cancellationToken);

            return new EvalRunResult(
                taskId,
                artifact.Status,
                artifact,
                sandboxRoot,
                transcriptPath,
                GetPageFiles(sandboxRoot),
                ReadIfExists(options.IndexPath));
        }
        catch (Exception ex)
        {
            if (ex is ModelCallTimeoutException timeoutEx)
            {
                EvalObservability.RecordSampleTimeout(
                    _logger,
                    runLabel,
                    EvalObservability.ProviderLabel(configuration.Kind),
                    configuration.Model,
                    timeoutEx.Timeout.TotalSeconds);
            }

            var safeMessage = SanitizeErrorText(DescribeExceptionChain(ex));
            var rollback = await journal.RollbackAsync(cancellationToken);
            var doc = new TaskArtifactDocument(
                TaskId: options.TaskId,
                Type: "ingest",
                Status: "failed",
                Agent: "ingest",
                StartedAt: startTime,
                CompletedAt: DateTimeOffset.UtcNow,
                SourceRef: options.SourceRef,
                PagesTouched: [],
                FailureReason: safeMessage,
                Narrative: $"Eval run failed: {safeMessage}",
                PagesCreated: [],
                PagesUpdated: [],
                PagesSuperseded: [],
                DeniedActions: executor.Denials.Select(d => new DeniedActionEntry(d.Action, d.RequestedTarget, d.CanonicalTarget, d.Reason, d.Turn)).ToList(),
                InstructionFiles: [new InstructionFileRecord(systemPromptDoc!.Path, systemPromptDoc.Sha256)],
                Policy: new PolicyRecord(loadedPolicy!.Identity.Path, loadedPolicy.Identity.Version, loadedPolicy.Identity.Sha256),
                Model: recordingModelClient.ModelId,
                Turns: null,
                RolledBack: rollback.Values.All(v => v),
                UserPromptSource: userPromptSource,
                UserPrompt: effectiveUserPrompt);

            await taskStore.WriteAsync(options.TaskArtifactPath, doc, cancellationToken);
            await logAppender.EnsureLogEntryAsync(options.LogPath, "failed", options.SourceRef, options.TaskId, true, cancellationToken);

            var transcriptPath = await WriteTranscriptAsync(taskId, recordingModelClient, cancellationToken);
            var artifact = await taskStore.ReadAsync(options.TaskArtifactPath, cancellationToken);

            return new EvalRunResult(
                taskId,
                artifact.Status,
                artifact,
                sandboxRoot,
                transcriptPath,
                GetPageFiles(sandboxRoot),
                ReadIfExists(options.IndexPath));
        }
    }

    private async Task<string> WriteTranscriptAsync(
        string taskId,
        RecordingModelClient recordingModelClient,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_transcriptRoot, $"{taskId}.json");
        var payload = JsonSerializer.Serialize(recordingModelClient.Calls, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, payload, cancellationToken);
        return path;
    }

    private static string[] GetPageFiles(string sandboxRoot)
    {
        var pagesDir = Path.Combine(sandboxRoot, "wiki", "pages");
        if (!Directory.Exists(pagesDir))
        {
            return [];
        }

        return Directory.GetFiles(pagesDir, "*.md", SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ReadIfExists(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <summary>
    /// Scrubs both eval-provider credential sources from an error message before it lands in
    /// FailureReason/Narrative (FR-008): the resolved ANTHROPIC_AUTH_TOKEN (which, for the
    /// affordable path, RunAsync sets from GRIMOIRE_EVAL_PROVIDER_API_KEY) and, defensively,
    /// the source variable itself. Mirrors Program.cs's SanitizeErrorText.
    /// </summary>
    internal static string SanitizeErrorText(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Unknown eval error.";
        }

        var sanitized = message;

        var anthropicToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        if (!string.IsNullOrWhiteSpace(anthropicToken))
        {
            sanitized = sanitized.Replace(anthropicToken, "[REDACTED]", StringComparison.Ordinal);
        }

        var providerApiKey = Environment.GetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY");
        if (!string.IsNullOrWhiteSpace(providerApiKey))
        {
            sanitized = sanitized.Replace(providerApiKey, "[REDACTED]", StringComparison.Ordinal);
        }

        sanitized = Regex.Replace(sanitized, "sk-ant-[A-Za-z0-9_-]+", "[REDACTED]", RegexOptions.CultureInvariant);
        return sanitized;
    }

    /// <summary>
    /// Walks the exception chain and joins each level's message (e.g. the Anthropic SDK's
    /// generic "I/O exception" wrapper plus the underlying "Connection refused" cause) so a
    /// connectivity failure reads as actionable (FR-004) rather than a bare wrapper message.
    /// </summary>
    private static string DescribeExceptionChain(Exception ex)
    {
        var messages = new List<string>();
        Exception? current = ex;
        while (current is not null && messages.Count < 4)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                messages.Add(current.Message);
            }

            current = current.InnerException;
        }

        return string.Join(" -> ", messages);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destinationDir, fileName), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(directory);
            CopyDirectory(directory, Path.Combine(destinationDir, dirName));
        }
    }

    private static string FindRepoRoot(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            var hasGit = Directory.Exists(Path.Combine(current.FullName, ".git"));
            var hasSpecify = Directory.Exists(Path.Combine(current.FullName, ".specify"));
            if (hasGit || hasSpecify)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root for eval tests.");
    }
}

public sealed class RecordingModelClient : IModelClient
{
    private readonly IModelClient _inner;
    private int _turn;

    public RecordingModelClient(IModelClient inner)
    {
        _inner = inner;
    }

    public string ModelId => _inner.ModelId;

    public List<RecordedEvalTurn> Calls { get; } = [];

    public async Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversation,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        var turn = await _inner.NextTurnAsync(systemPrompt, conversation, tools, cancellationToken);
        _turn++;

        Calls.Add(new RecordedEvalTurn(
            _turn,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(systemPrompt))),
            conversation.Select(c => new RecordedMessage(c.Role, c.Content)).ToList(),
            tools.Select(t => t.Name).ToList(),
            turn.StopReason,
            turn.ToolUseRequests.Select(t => new RecordedToolUse(t.ToolUseId, t.ToolName, t.InputJson)).ToList(),
            turn.AssistantText,
            turn.InputTokens,
            turn.OutputTokens));

        return turn;
    }
}

public sealed record RecordedEvalTurn(
    int Turn,
    string SystemPromptSha256,
    IReadOnlyList<RecordedMessage> Conversation,
    IReadOnlyList<string> ToolNames,
    ModelStopReason StopReason,
    IReadOnlyList<RecordedToolUse> ToolUses,
    string? AssistantText,
    int InputTokens,
    int OutputTokens);

public sealed record RecordedMessage(string Role, string Content);

public sealed record RecordedToolUse(string ToolUseId, string ToolName, string InputJson);
