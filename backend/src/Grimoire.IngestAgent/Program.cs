using Grimoire.AgentRuntime.Composition;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Host;
using Grimoire.AgentRuntime.Instructions;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.AgentRuntime.Telemetry;
using Grimoire.IngestAgent;
using Grimoire.IngestAgent.IngestLog;
using Grimoire.IngestAgent.Source;
using Grimoire.IngestAgent.TaskArtifact;
using Microsoft.Extensions.Logging;

// Composition root (ADR-013): the Ingest Agent Profile — identity, frozen telemetry
// identities, explicit tool set, required instruction documents, and ADR-004 model
// env-var names — plus the Ingest intent hooks, running on the shared AgentHost
// template. CLI surface, NDJSON event sequence, exit codes, and artifact behavior are
// byte-identical to the pre-consolidation host (FR-008; ADR-002/008 contracts).
var profile = new AgentProfile(
    AgentName: "ingest",
    ServiceName: "Grimoire.IngestAgent",
    ActivitySourceName: "Grimoire.IngestAgent",
    MeterName: "Grimoire.IngestAgent",
    RunSpanName: "ingest_agent.run",
    CorrelationAttribute: "task_id",
    ToolRegistry: IngestToolRegistry.Default,
    RequiredInstructionDocuments: new HashSet<InstructionDocument>
    {
        InstructionDocument.SystemPrompt,
        InstructionDocument.DefaultUserPrompt,
    },
    ModelEnvVarNames: new ModelEnvVarNames("GRIMOIRE_INGEST_MODEL", "GRIMOIRE_INGEST_BASE_URL"));

using var telemetry = AgentTelemetryBootstrap.Build(profile.ServiceName, profile.ActivitySourceName, profile.MeterName);
var loggerFactory = telemetry.LoggerFactory;
var logger = loggerFactory.CreateLogger("Grimoire.IngestAgent.Program");

var options = ReadCliOptions(args);
// Stdout is the NDJSON event channel (ADR-008); all logging goes to stderr/OTLP.
using var runEvents = new RunEventEmitter(Console.Out, options.TaskId);
var taskStore = new TaskArtifactStore();
var logAppender = new IngestLogAppender(loggerFactory.CreateLogger<IngestLogAppender>());
var sourceReader = new SourceReader();

var startTime = DateTimeOffset.UtcNow;
using var runSpan = IngestAgentTracing.StartRunActivity(options.TaskId);

// 004 FR-014: convert-step configuration is Hub-owned and set at submission time.
// Read it from whatever the Hub already wrote before this process's first write
// overwrites the file, then carry it forward verbatim into every subsequent write
// so it survives the agent taking over the artifact.
IReadOnlyDictionary<string, bool>? convertSteps = null;
if (File.Exists(options.TaskArtifactPath))
{
    try
    {
        var preExisting = await taskStore.ReadAsync(options.TaskArtifactPath, CancellationToken.None);
        convertSteps = preExisting.ConvertSteps;
    }
    catch
    {
        // Not yet a valid artifact (e.g. manual CLI run with no prior Hub write) — no
        // convert-step configuration to carry forward.
    }
}

var intent = new IngestIntentHandler(
    profile, options, runEvents, taskStore, logAppender, sourceReader,
    loggerFactory, logger, startTime, convertSteps);

return await new AgentHost(profile).RunAsync(
    new AgentHostRun(
        WikiRoot: options.WikiRoot,
        SystemPromptPath: options.SystemPromptPath,
        PolicyPath: options.PolicyPath,
        HeartbeatSeconds: options.HeartbeatSeconds,
        DefaultUserPromptPath: options.DefaultUserPromptPath,
        UserPromptOverride: options.UserPrompt),
    runEvents,
    intent,
    CancellationToken.None);

static IngestCliOptions ReadCliOptions(string[] args)
{
    var reader = new AgentArgumentReader(args);

    var sourceKind = reader.GetRequired("--source-kind");
    string? pastedText = null;
    if (sourceKind == "pasted_text")
        pastedText = Console.In.ReadToEnd();

    return new IngestCliOptions(
        TaskId: reader.GetRequired("--task-id"),
        SourceRef: reader.GetRequired("--source-ref"),
        SourceKind: sourceKind,
        WikiRoot: reader.GetRequired("--wiki-root"),
        PagesDir: reader.GetRequired("--pages-dir"),
        TasksDir: reader.GetRequired("--tasks-dir"),
        IndexPath: reader.GetRequired("--index-path"),
        LogPath: reader.GetRequired("--log-path"),
        PastedText: pastedText,
        SystemPromptPath: reader.GetRequired("--system-prompt-path"),
        DefaultUserPromptPath: reader.GetRequired("--default-user-prompt-path"),
        UserPrompt: reader.GetOptional("--user-prompt"),
        PolicyPath: reader.GetRequired("--policy-path"),
        HeartbeatSeconds: reader.GetHeartbeatSeconds());
}

/// <summary>
/// The Ingest intent hooks (ADR-013): task-artifact lifecycle, ingest-log appending,
/// source reading, rollback/all-denied failure handling, and user-prompt-resolution
/// logging — the code that differs because the intent differs. Behavior is
/// byte-identical to the pre-consolidation inline sequencing (FR-008).
/// </summary>
internal sealed class IngestIntentHandler : IAgentIntentHandler
{
    private readonly AgentProfile _profile;
    private readonly IngestCliOptions _options;
    private readonly RunEventEmitter _runEvents;
    private readonly TaskArtifactStore _taskStore;
    private readonly IngestLogAppender _logAppender;
    private readonly SourceReader _sourceReader;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly DateTimeOffset _startTime;
    private readonly IReadOnlyDictionary<string, bool>? _convertSteps;
    private readonly WriteJournal _journal = new();

    private IModelClient? _modelClient;
    private GuardedToolExecutor? _executor;
    private LoadedInstructions? _instructions;

    public IngestIntentHandler(
        AgentProfile profile,
        IngestCliOptions options,
        RunEventEmitter runEvents,
        TaskArtifactStore taskStore,
        IngestLogAppender logAppender,
        SourceReader sourceReader,
        ILoggerFactory loggerFactory,
        ILogger logger,
        DateTimeOffset startTime,
        IReadOnlyDictionary<string, bool>? convertSteps)
    {
        _profile = profile;
        _options = options;
        _runEvents = runEvents;
        _taskStore = taskStore;
        _logAppender = logAppender;
        _sourceReader = sourceReader;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _startTime = startTime;
        _convertSteps = convertSteps;
    }

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        _modelClient = ModelClientFactory.Create(_loggerFactory, _profile.ModelEnvVarNames);

        await _taskStore.WriteAsync(
            _options.TaskArtifactPath,
            new TaskArtifactDocument(
                TaskId: _options.TaskId,
                Type: "ingest",
                Status: "running",
                Agent: "ingest",
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: null,
                SourceRef: _options.SourceRef,
                PagesTouched: [],
                FailureReason: null,
                Narrative: $"Ingest started for source: {_options.SourceRef}",
                ConvertSteps: _convertSteps),
            CancellationToken.None);
    }

    public async Task OnInstructionLoadFailureAsync(
        string documentKind, string documentPath, string reason, CancellationToken cancellationToken)
    {
        IngestAgentMetrics.RecordInstructionLoadFailure(documentKind);
        IngestAgentLogEvents.LogInstructionsLoadFailed(
            _logger,
            _options.TaskId,
            documentKind,
            documentPath,
            reason);
        await FinalizeFailedAsync(reason, journal: null, rolledBack: false, modelId: _modelClient!.ModelId);
    }

    public Task OnInstructionsLoadedAsync(LoadedInstructions instructions, CancellationToken cancellationToken)
    {
        _instructions = instructions;

        IngestAgentLogEvents.LogInstructionsLoaded(
            _logger,
            _options.TaskId,
            instructions.SystemPrompt.Path,
            instructions.SystemPrompt.Sha256,
            instructions.Policy.Identity.Version,
            instructions.Policy.Identity.Sha256);

        IngestAgentLogEvents.LogUserPromptResolved(
            _logger,
            _options.TaskId,
            instructions.UserPromptSource!,
            instructions.EffectiveUserPrompt!.Length);

        // Block-scoped so the span closes here; later model_turn spans must parent
        // to ingest_agent.run, not to load_instructions.
        using (var loadSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.load_instructions"))
        {
            loadSpan?.SetTag("task_id", _options.TaskId);
            loadSpan?.SetTag("system_prompt_sha256", instructions.SystemPrompt.Sha256);
            loadSpan?.SetTag("prompt_source", instructions.UserPromptSource);
        }

        return Task.CompletedTask;
    }

    public async Task<int> ExecuteAsync(LoadedInstructions instructions, CancellationToken cancellationToken)
    {
        var readSource = await _sourceReader.ReadAsync(
            _options.SourceKind, _options.SourceRef, _options.PastedText, CancellationToken.None);

        _executor = new GuardedToolExecutor(
            instructions.Policy.Policy,
            _journal,
            _options.WikiRoot,
            taskId: _options.TaskId,
            registry: _profile.ToolRegistry,
            instrumentation: new IngestToolCallInstrumentation(_loggerFactory.CreateLogger<GuardedToolExecutor>()));
        var tokenCap = ResolveTokenCapFromEnvironment();
        var loop = new AgentLoop(
            _modelClient!,
            _executor,
            tokenCap: tokenCap,
            eventEmitter: _runEvents,
            registry: _profile.ToolRegistry,
            instrumentation: new IngestAgentLoopInstrumentation());
        var systemPrompt = instructions.SystemPrompt.Content;

        AgentLoopResult loopResult;
        try
        {
            loopResult = await loop.RunAsync(
                systemPrompt, instructions.EffectiveUserPrompt!, _options.TaskId, _options.SourceRef, readSource.Content, CancellationToken.None);
        }
        catch (AgentLoopCapException capEx)
        {
            IngestAgentLogEvents.LogAgentCapExceeded(_logger, _options.TaskId, capEx.Cap, capEx.TurnsUsed);
            var rollbackOutcome = await RollbackAsync();
            await FinalizeFailedAsync(
                capEx.Message, _journal, rolledBack: rollbackOutcome,
                systemPrompt: instructions.SystemPrompt, policy: instructions.Policy,
                modelId: _modelClient!.ModelId, deniedActions: _executor.Denials,
                userPromptSource: instructions.UserPromptSource, userPrompt: instructions.EffectiveUserPrompt);
            _runEvents.EmitFailed(capEx.Message);
            return 1;
        }

        if (_journal.TouchedPaths.Count == 0 && _executor.Denials.Count > 0)
        {
            const string allDeniedReason = "All attempted write actions were denied by the safety policy; no result was produced.";
            await FinalizeFailedAsync(
                allDeniedReason, _journal, rolledBack: false,
                systemPrompt: instructions.SystemPrompt, policy: instructions.Policy,
                modelId: _modelClient!.ModelId, deniedActions: _executor.Denials,
                userPromptSource: instructions.UserPromptSource, userPrompt: instructions.EffectiveUserPrompt);
            _runEvents.EmitFailed(allDeniedReason);
            return 1;
        }

        var touchedPaths = _journal.TouchedPaths;
        var pagesCreated = _journal.CreatedPaths;
        var pagesUpdated = _journal.UpdatedPaths;
        var pagesSuperseded = _journal.SupersededPaths;
        var wikiRoot = _options.WikiRoot;

        using var finalizeSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.finalize_artifact");
        finalizeSpan?.SetTag("task_id", _options.TaskId);
        finalizeSpan?.SetTag("outcome", "completed");

        await _taskStore.WriteAsync(
            _options.TaskArtifactPath,
            new TaskArtifactDocument(
                TaskId: _options.TaskId,
                Type: "ingest",
                Status: "completed",
                Agent: "ingest",
                StartedAt: _startTime,
                CompletedAt: DateTimeOffset.UtcNow,
                SourceRef: _options.SourceRef,
                PagesTouched: touchedPaths.Select(p => Path.GetRelativePath(wikiRoot, p)).ToList(),
                FailureReason: null,
                Narrative: loopResult.Narrative,
                PagesCreated: pagesCreated.Select(p => Path.GetRelativePath(wikiRoot, p)).ToList(),
                PagesUpdated: pagesUpdated.Select(p => Path.GetRelativePath(wikiRoot, p)).ToList(),
                PagesSuperseded: pagesSuperseded.Select(p => Path.GetRelativePath(wikiRoot, p)).ToList(),
                DeniedActions: _executor.Denials.Select(d => new DeniedActionEntry(d.Action, d.RequestedTarget, d.CanonicalTarget, d.Reason, d.Turn)).ToList(),
                InstructionFiles: [new InstructionFileRecord(instructions.SystemPrompt.Path, instructions.SystemPrompt.Sha256)],
                Policy: new PolicyRecord(instructions.Policy.Identity.Path, instructions.Policy.Identity.Version, instructions.Policy.Identity.Sha256),
                Model: _modelClient!.ModelId,
                Turns: loopResult.TurnsUsed,
                RolledBack: null,
                UserPromptSource: instructions.UserPromptSource,
                UserPrompt: instructions.EffectiveUserPrompt,
                ConvertSteps: _convertSteps),
            CancellationToken.None);

        await _logAppender.EnsureLogEntryAsync(
            _options.LogPath, "completed", _options.SourceRef, _options.TaskId,
            forceAppend: false, CancellationToken.None);

        IngestAgentLogEvents.LogAgentCompleted(
            _logger,
            _options.TaskId,
            loopResult.TurnsUsed,
            _journal,
            _executor.Denials.Count);

        IngestAgentMetrics.RecordPagesTouched(_journal);
        IngestAgentMetrics.RecordIngest("completed",
            (DateTimeOffset.UtcNow - _startTime).TotalSeconds);

        _runEvents.EmitCompleted(loopResult.Narrative);
        return 0;
    }

    public async Task<string> DescribeUnhandledFailureAsync(Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Ingest agent failed for task {TaskId}.", _options.TaskId);

        var safeMessage = ErrorSanitizer.Sanitize(exception.Message, "Unknown ingest error.");
        var rollbackOutcome = await RollbackAsync();
        await FinalizeFailedAsync(
            safeMessage, _journal, rolledBack: rollbackOutcome,
            modelId: _modelClient?.ModelId, deniedActions: _executor?.Denials);
        return safeMessage;
    }

    private async Task<bool> RollbackAsync()
    {
        using var rollbackSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.rollback");
        rollbackSpan?.SetTag("task_id", _options.TaskId);
        try
        {
            var outcomes = await _journal.RollbackAsync(CancellationToken.None);
            var allOk = outcomes.Values.All(ok => ok);
            IngestAgentMetrics.RecordRollback(allOk);
            rollbackSpan?.SetTag("paths_restored", outcomes.Count);
            IngestAgentLogEvents.LogRunRolledBack(_logger, _options.TaskId, outcomes.Count, allOk);
            return allOk;
        }
        catch
        {
            IngestAgentMetrics.RecordRollback(false);
            IngestAgentLogEvents.LogRunRolledBack(_logger, _options.TaskId, 0, false);
            return false;
        }
    }

    private async Task FinalizeFailedAsync(
        string failureReason,
        WriteJournal? journal,
        bool rolledBack,
        LoadedSystemPrompt? systemPrompt = null,
        LoadedPolicy? policy = null,
        string? modelId = null,
        IReadOnlyList<DeniedActionRecord>? deniedActions = null,
        string? userPromptSource = null,
        string? userPrompt = null)
    {
        using var finalizeSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.finalize_artifact");
        finalizeSpan?.SetTag("task_id", _options.TaskId);
        finalizeSpan?.SetTag("outcome", "failed");

        await _taskStore.WriteAsync(
            _options.TaskArtifactPath,
            new TaskArtifactDocument(
                TaskId: _options.TaskId,
                Type: "ingest",
                Status: "failed",
                Agent: "ingest",
                StartedAt: _startTime,
                CompletedAt: DateTimeOffset.UtcNow,
                SourceRef: _options.SourceRef,
                PagesTouched: [],
                FailureReason: failureReason,
                Narrative: $"Ingest failed: {failureReason}",
                PagesCreated: [],
                PagesUpdated: [],
                PagesSuperseded: [],
                DeniedActions: deniedActions?.Select(d => new DeniedActionEntry(d.Action, d.RequestedTarget, d.CanonicalTarget, d.Reason, d.Turn)).ToList() ?? [],
                InstructionFiles: systemPrompt is null ? null : [new InstructionFileRecord(systemPrompt.Path, systemPrompt.Sha256)],
                Policy: policy is null ? null : new PolicyRecord(policy.Identity.Path, policy.Identity.Version, policy.Identity.Sha256),
                Model: modelId,
                Turns: null,
                RolledBack: journal is not null ? rolledBack : null,
                UserPromptSource: userPromptSource,
                UserPrompt: userPrompt,
                ConvertSteps: _convertSteps),
            CancellationToken.None);

        await _logAppender.EnsureLogEntryAsync(
            _options.LogPath, "failed", _options.SourceRef, _options.TaskId,
            forceAppend: true, CancellationToken.None);

        IngestAgentMetrics.RecordIngest("failed",
            (DateTimeOffset.UtcNow - _startTime).TotalSeconds);
    }

    private static int ResolveTokenCapFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("GRIMOIRE_INGEST_TOKEN_CAP");
        if (string.IsNullOrWhiteSpace(raw))
            return 200_000;

        if (int.TryParse(raw, out var parsed) && parsed > 0)
            return parsed;

        return 200_000;
    }
}
