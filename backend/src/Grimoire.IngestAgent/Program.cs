using Grimoire.IngestAgent;
using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent.Guardrails;
using Grimoire.IngestAgent.IngestLog;
using Grimoire.IngestAgent.Source;
using Grimoire.IngestAgent.TaskArtifact;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

using var telemetry = TelemetryBootstrap.Build();
var loggerFactory = telemetry.LoggerFactory;
var logger = loggerFactory.CreateLogger("Grimoire.IngestAgent.Program");

var options = ParseArgs(args);
// Stdout is the NDJSON event channel (ADR-008); all logging goes to stderr/OTLP.
using var runEvents = new RunEventEmitter(Console.Out, options.TaskId);
var taskStore = new TaskArtifactStore();
var logAppender = new IngestLogAppender(loggerFactory.CreateLogger<IngestLogAppender>());
var sourceReader = new SourceReader();

var startTime = DateTimeOffset.UtcNow;
using var runSpan = IngestAgentTracing.StartRunActivity(options.TaskId);

var repoRoot = FindRepoRoot(options.TasksDir);
var journal = new WriteJournal();
GuardedToolExecutor? executor = null;
AnthropicModelClient? modelClient = null;

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

try
{
    modelClient = new AnthropicModelClient();

    await taskStore.WriteAsync(
        options.TaskArtifactPath,
        new TaskArtifactDocument(
            TaskId: options.TaskId,
            Type: "ingest",
            Status: "running",
            Agent: "ingest",
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            SourceRef: options.SourceRef,
            PagesTouched: [],
            FailureReason: null,
            Narrative: $"Ingest started for source: {options.SourceRef}",
            ConvertSteps: convertSteps),
        CancellationToken.None);

    var promptLoader = new SystemPromptLoader();
    var systemPromptResult = await promptLoader.LoadAsync(options.SystemPromptPath, CancellationToken.None);
    if (systemPromptResult.IsSecond(out var systemPromptFailure))
    {
        IngestAgentMetrics.RecordInstructionLoadFailure("instructions");
        IngestAgentLogEvents.LogInstructionsLoadFailed(
            logger,
            options.TaskId,
            "instructions",
            options.SystemPromptPath,
            systemPromptFailure.Reason);
        await FinalizeFailedAsync(taskStore, options, startTime, systemPromptFailure.Reason, null, logAppender, false, modelId: modelClient.ModelId, convertSteps: convertSteps);
        runEvents.EmitFailed(systemPromptFailure.Reason);
        return 1;
    }
    systemPromptResult.IsFirst(out var loadedSystemPrompt);

    // Effective user prompt: explicit --user-prompt override, else the versioned
    // default-user-prompt document. No override + missing/empty default ⇒ fail closed
    // (ADR-007).
    string effectiveUserPrompt;
    string userPromptSource;
    if (!string.IsNullOrWhiteSpace(options.UserPrompt))
    {
        effectiveUserPrompt = options.UserPrompt.Trim();
        userPromptSource = "custom";
    }
    else
    {
        var defaultPromptResult = await promptLoader.LoadAsync(options.DefaultUserPromptPath, CancellationToken.None);
        if (defaultPromptResult.IsSecond(out var defaultPromptFailure))
        {
            IngestAgentMetrics.RecordInstructionLoadFailure("default_user_prompt");
            IngestAgentLogEvents.LogInstructionsLoadFailed(
                logger,
                options.TaskId,
                "default_user_prompt",
                options.DefaultUserPromptPath,
                defaultPromptFailure.Reason);
            await FinalizeFailedAsync(taskStore, options, startTime, defaultPromptFailure.Reason, null, logAppender, false, modelId: modelClient.ModelId, convertSteps: convertSteps);
            runEvents.EmitFailed(defaultPromptFailure.Reason);
            return 1;
        }
        defaultPromptResult.IsFirst(out var loadedDefaultPrompt);
        effectiveUserPrompt = loadedDefaultPrompt!.Content.Trim();
        userPromptSource = "default";
    }

    var policyLoader = new PolicyLoader(repoRoot);
    var policyResult = await policyLoader.LoadAsync(options.PolicyPath, CancellationToken.None);
    if (policyResult.IsSecond(out var policyFailure))
    {
        IngestAgentMetrics.RecordInstructionLoadFailure("policy");
        IngestAgentLogEvents.LogInstructionsLoadFailed(
            logger,
            options.TaskId,
            "policy",
            options.PolicyPath,
            policyFailure.Reason);
        await FinalizeFailedAsync(taskStore, options, startTime, policyFailure.Reason, null, logAppender, false, modelId: modelClient.ModelId, convertSteps: convertSteps);
        runEvents.EmitFailed(policyFailure.Reason);
        return 1;
    }
    policyResult.IsFirst(out var loadedPolicy);

    IngestAgentLogEvents.LogInstructionsLoaded(
        logger,
        options.TaskId,
        loadedSystemPrompt!.Path,
        loadedSystemPrompt.Sha256,
        loadedPolicy!.Identity.Version,
        loadedPolicy.Identity.Sha256);

    IngestAgentLogEvents.LogUserPromptResolved(
        logger,
        options.TaskId,
        userPromptSource,
        effectiveUserPrompt.Length);

    // Block-scoped so the span closes here; later model_turn spans must parent
    // to ingest_agent.run, not to load_instructions.
    using (var loadSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.load_instructions"))
    {
        loadSpan?.SetTag("task_id", options.TaskId);
        loadSpan?.SetTag("system_prompt_sha256", loadedSystemPrompt.Sha256);
        loadSpan?.SetTag("prompt_source", userPromptSource);
    }

    // Event channel goes live once instructions and policy are loaded (contract: started
    // first, then heartbeats independent of model latency).
    runEvents.EmitStarted();
    runEvents.StartHeartbeat(TimeSpan.FromSeconds(options.HeartbeatSeconds));

    var readSource = await sourceReader.ReadAsync(
        options.SourceKind, options.SourceRef, options.PastedText, CancellationToken.None);

    executor = new GuardedToolExecutor(
        loadedPolicy!.Policy,
        journal,
        repoRoot,
        options.TaskId,
        loggerFactory.CreateLogger<GuardedToolExecutor>());
    var tokenCap = ResolveTokenCapFromEnvironment();
    var loop = new AgentLoop(modelClient, executor, tokenCap: tokenCap, eventEmitter: runEvents);
    var systemPrompt = loadedSystemPrompt.Content;

    AgentLoopResult loopResult;
    try
    {
        loopResult = await loop.RunAsync(
            systemPrompt, effectiveUserPrompt, options.TaskId, options.SourceRef, readSource.Content, CancellationToken.None);
    }
    catch (AgentLoopCapException capEx)
    {
        IngestAgentLogEvents.LogAgentCapExceeded(logger, options.TaskId, capEx.Cap, capEx.TurnsUsed);
        var rollbackOutcome = await RollbackAsync(journal, options.TaskId, logger);
        await FinalizeFailedAsync(taskStore, options, startTime, capEx.Message, journal, logAppender, rollbackOutcome, loadedSystemPrompt, loadedPolicy, modelClient.ModelId, executor.Denials, userPromptSource, effectiveUserPrompt, convertSteps);
        runEvents.EmitFailed(capEx.Message);
        return 1;
    }

    if (journal.TouchedPaths.Count == 0 && executor.Denials.Count > 0)
    {
        const string allDeniedReason = "All attempted write actions were denied by the safety policy; no result was produced.";
        await FinalizeFailedAsync(
            taskStore,
            options,
            startTime,
            allDeniedReason,
            journal,
            logAppender,
            rolledBack: false,
            loadedSystemPrompt,
            loadedPolicy,
            modelClient.ModelId,
            executor.Denials,
            userPromptSource,
            effectiveUserPrompt,
            convertSteps);
        runEvents.EmitFailed(allDeniedReason);
        return 1;
    }

    var touchedPaths = journal.TouchedPaths;
    var pagesCreated = journal.CreatedPaths;
    var pagesUpdated = journal.UpdatedPaths;
    var pagesSuperseded = journal.SupersededPaths;

    using var finalizeSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.finalize_artifact");
    finalizeSpan?.SetTag("task_id", options.TaskId);
    finalizeSpan?.SetTag("outcome", "completed");

    await taskStore.WriteAsync(
        options.TaskArtifactPath,
        new TaskArtifactDocument(
            TaskId: options.TaskId,
            Type: "ingest",
            Status: "completed",
            Agent: "ingest",
            StartedAt: startTime,
            CompletedAt: DateTimeOffset.UtcNow,
            SourceRef: options.SourceRef,
            PagesTouched: touchedPaths.Select(p => Path.GetRelativePath(repoRoot, p)).ToList(),
            FailureReason: null,
            Narrative: loopResult.Narrative,
            PagesCreated: pagesCreated.Select(p => Path.GetRelativePath(repoRoot, p)).ToList(),
            PagesUpdated: pagesUpdated.Select(p => Path.GetRelativePath(repoRoot, p)).ToList(),
            PagesSuperseded: pagesSuperseded.Select(p => Path.GetRelativePath(repoRoot, p)).ToList(),
            DeniedActions: executor.Denials.Select(d => new DeniedActionEntry(d.Action, d.RequestedTarget, d.CanonicalTarget, d.Reason, d.Turn)).ToList(),
            InstructionFiles: [new InstructionFileRecord(loadedSystemPrompt.Path, loadedSystemPrompt.Sha256)],
            Policy: new PolicyRecord(loadedPolicy.Identity.Path, loadedPolicy.Identity.Version, loadedPolicy.Identity.Sha256),
            Model: modelClient.ModelId,
            Turns: loopResult.TurnsUsed,
            RolledBack: null,
            UserPromptSource: userPromptSource,
            UserPrompt: effectiveUserPrompt,
            ConvertSteps: convertSteps),
        CancellationToken.None);

    await logAppender.EnsureLogEntryAsync(
        options.LogPath, "completed", options.SourceRef, options.TaskId,
        forceAppend: false, CancellationToken.None);

    IngestAgentLogEvents.LogAgentCompleted(
        logger,
        options.TaskId,
        loopResult.TurnsUsed,
        journal,
        executor.Denials.Count);

    IngestAgentMetrics.RecordPagesTouched(journal);
    IngestAgentMetrics.RecordIngest("completed",
        (DateTimeOffset.UtcNow - startTime).TotalSeconds);

    runEvents.EmitCompleted(loopResult.Narrative);
    return 0;
}
catch (Exception ex)
{
    var safeMessage = SanitizeErrorText(ex.Message);
    var rollbackOutcome = await RollbackAsync(journal, options.TaskId, logger);
    await FinalizeFailedAsync(taskStore, options, startTime, safeMessage, journal, logAppender, rollbackOutcome, modelId: modelClient?.ModelId, deniedActions: executor?.Denials, convertSteps: convertSteps);
    runEvents.EmitFailed(safeMessage);
    return 1;
}

static async Task<bool> RollbackAsync(WriteJournal journal, string taskId, ILogger logger)
{
    using var rollbackSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.rollback");
    rollbackSpan?.SetTag("task_id", taskId);
    try
    {
        var outcomes = await journal.RollbackAsync(CancellationToken.None);
        var allOk = outcomes.Values.All(ok => ok);
        IngestAgentMetrics.RecordRollback(allOk);
        rollbackSpan?.SetTag("paths_restored", outcomes.Count);
        IngestAgentLogEvents.LogRunRolledBack(logger, taskId, outcomes.Count, allOk);
        return allOk;
    }
    catch
    {
        IngestAgentMetrics.RecordRollback(false);
        IngestAgentLogEvents.LogRunRolledBack(logger, taskId, 0, false);
        return false;
    }
}

static async Task FinalizeFailedAsync(
    TaskArtifactStore taskStore,
    AgentCliOptions options,
    DateTimeOffset startTime,
    string failureReason,
    WriteJournal? journal,
    IngestLogAppender logAppender,
    bool rolledBack,
    LoadedSystemPrompt? systemPrompt = null,
    LoadedPolicy? policy = null,
    string? modelId = null,
    IReadOnlyList<DeniedActionRecord>? deniedActions = null,
    string? userPromptSource = null,
    string? userPrompt = null,
    IReadOnlyDictionary<string, bool>? convertSteps = null)
{
    using var finalizeSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.finalize_artifact");
    finalizeSpan?.SetTag("task_id", options.TaskId);
    finalizeSpan?.SetTag("outcome", "failed");

    await taskStore.WriteAsync(
        options.TaskArtifactPath,
        new TaskArtifactDocument(
            TaskId: options.TaskId,
            Type: "ingest",
            Status: "failed",
            Agent: "ingest",
            StartedAt: startTime,
            CompletedAt: DateTimeOffset.UtcNow,
            SourceRef: options.SourceRef,
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
            ConvertSteps: convertSteps),
        CancellationToken.None);

    await logAppender.EnsureLogEntryAsync(
        options.LogPath, "failed", options.SourceRef, options.TaskId,
        forceAppend: true, CancellationToken.None);

    IngestAgentMetrics.RecordIngest("failed",
        (DateTimeOffset.UtcNow - startTime).TotalSeconds);
}

static string SanitizeErrorText(string message)
{
    if (string.IsNullOrWhiteSpace(message))
        return "Unknown ingest error.";

    var sanitized = message;
    var envAuthToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
    if (!string.IsNullOrWhiteSpace(envAuthToken))
        sanitized = sanitized.Replace(envAuthToken, "[REDACTED]", StringComparison.Ordinal);

    sanitized = Regex.Replace(sanitized, "sk-ant-[A-Za-z0-9_-]+", "[REDACTED]",
        RegexOptions.CultureInvariant);
    return sanitized;
}

static AgentCliOptions ParseArgs(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < args.Length - 1; i += 2)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal))
            options[args[i]] = args[i + 1];
    }

    string GetRequired(string name)
        => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required argument {name}");

    var sourceKind = GetRequired("--source-kind");
    string? pastedText = null;
    if (sourceKind == "pasted_text")
        pastedText = Console.In.ReadToEnd();

    string? GetOptional(string name)
        => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    var heartbeatSeconds = int.TryParse(GetOptional("--heartbeat-seconds"), out var parsedHeartbeat) && parsedHeartbeat > 0
        ? parsedHeartbeat
        : 10;

    return new AgentCliOptions(
        TaskId: GetRequired("--task-id"),
        SourceRef: GetRequired("--source-ref"),
        SourceKind: sourceKind,
        PagesDir: GetRequired("--pages-dir"),
        TasksDir: GetRequired("--tasks-dir"),
        IndexPath: GetRequired("--index-path"),
        LogPath: GetRequired("--log-path"),
        PastedText: pastedText,
        SystemPromptPath: GetRequired("--system-prompt-path"),
        DefaultUserPromptPath: GetRequired("--default-user-prompt-path"),
        UserPrompt: GetOptional("--user-prompt"),
        PolicyPath: GetRequired("--policy-path"),
        HeartbeatSeconds: heartbeatSeconds);
}

static string FindRepoRoot(string startPath)
{
    var current = Path.GetFullPath(startPath);
    while (true)
    {
        if (Directory.Exists(Path.Combine(current, ".specify")) &&
            Directory.Exists(Path.Combine(current, "specs")))
            return current;

        if (Directory.Exists(Path.Combine(current, ".git")))
            return current;

        var parent = Directory.GetParent(current);
        if (parent is null)
            return Path.GetFullPath(Path.Combine(startPath, "..", ".."));

        current = parent.FullName;
    }
}

static int ResolveTokenCapFromEnvironment()
{
    var raw = Environment.GetEnvironmentVariable("GRIMOIRE_INGEST_TOKEN_CAP");
    if (string.IsNullOrWhiteSpace(raw))
        return 200_000;

    if (int.TryParse(raw, out var parsed) && parsed > 0)
        return parsed;

    return 200_000;
}
