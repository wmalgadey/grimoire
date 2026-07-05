using Grimoire.IngestAgent;
using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent.Guardrails;
using Grimoire.IngestAgent.IngestLog;
using Grimoire.IngestAgent.Source;
using Grimoire.IngestAgent.TaskArtifact;
using System.Text.RegularExpressions;

using var telemetry = TelemetryBootstrap.Build();

var options = ParseArgs(args);
var taskStore = new TaskArtifactStore();
var logAppender = new IngestLogAppender();
var sourceReader = new SourceReader();

var startTime = DateTimeOffset.UtcNow;
using var runSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.run");
runSpan?.SetTag("task_id", options.TaskId);

var repoRoot = FindRepoRoot(options.TasksDir);
var journal = new WriteJournal();
AnthropicModelClient? modelClient = null;

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
			Narrative: $"Ingest started for source: {options.SourceRef}"),
		CancellationToken.None);

	var instructionLoader = new InstructionSetLoader();
	var instructionResult = await instructionLoader.LoadAsync(options.InstructionsDir, CancellationToken.None);
	if (instructionResult.IsSecond(out var instructionFailure))
	{
		IngestAgentMetrics.RecordInstructionLoadFailure("instructions");
		await FinalizeFailedAsync(taskStore, options, startTime, instructionFailure.Reason, null, logAppender, false, modelId: modelClient.ModelId);
		return 1;
	}
	instructionResult.IsFirst(out var instructionSet);

	var policyLoader = new PolicyLoader(repoRoot);
	var policyResult = await policyLoader.LoadAsync(options.PolicyPath, CancellationToken.None);
	if (policyResult.IsSecond(out var policyFailure))
	{
		IngestAgentMetrics.RecordInstructionLoadFailure("policy");
		await FinalizeFailedAsync(taskStore, options, startTime, policyFailure.Reason, null, logAppender, false, modelId: modelClient.ModelId);
		return 1;
	}
	policyResult.IsFirst(out var loadedPolicy);

	using var loadSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.load_instructions");
	loadSpan?.SetTag("task_id", options.TaskId);
	loadSpan?.SetTag("file_count", instructionSet!.Files.Count);

	var readSource = await sourceReader.ReadAsync(
		options.SourceKind, options.SourceRef, options.PastedText, CancellationToken.None);

	var executor = new GuardedToolExecutor(loadedPolicy!.Policy, journal, repoRoot);
	var tokenCap = ResolveTokenCapFromEnvironment();
	var loop = new AgentLoop(modelClient, executor, tokenCap: tokenCap);
	var systemPrompt = instructionSet.BuildSystemPrompt();

	AgentLoopResult loopResult;
	try
	{
		loopResult = await loop.RunAsync(
			systemPrompt, options.TaskId, options.SourceRef, readSource.Content, CancellationToken.None);
	}
	catch (AgentLoopCapException capEx)
	{
		var rollbackOutcome = await RollbackAsync(journal);
		await FinalizeFailedAsync(taskStore, options, startTime, capEx.Message, journal, logAppender, rollbackOutcome, instructionSet, loadedPolicy, modelClient.ModelId);
		return 1;
	}

	var touchedPaths = executor.TouchedPaths;

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
			PagesCreated: touchedPaths.Select(p => Path.GetRelativePath(repoRoot, p)).ToList(),
			PagesUpdated: [],
			PagesSuperseded: [],
			DeniedActions: executor.Denials.Select(d => new DeniedActionEntry(d.Action, d.RequestedTarget, d.CanonicalTarget, d.Reason, d.Turn)).ToList(),
			InstructionFiles: instructionSet.Files.Select(f => new InstructionFileRecord(f.Path, f.Sha256)).ToList(),
			Policy: new PolicyRecord(loadedPolicy.Identity.Path, loadedPolicy.Identity.Version, loadedPolicy.Identity.Sha256),
			Model: modelClient.ModelId,
			Turns: loopResult.TurnsUsed,
			RolledBack: null),
		CancellationToken.None);

	await logAppender.EnsureLogEntryAsync(
		options.LogPath, "completed", options.SourceRef, options.TaskId,
		forceAppend: false, CancellationToken.None);

	IngestAgentMetrics.RecordIngest("completed", touchedPaths.Count, "touched",
		(DateTimeOffset.UtcNow - startTime).TotalSeconds);

	return 0;
}
catch (Exception ex)
{
	var safeMessage = SanitizeErrorText(ex.Message);
	var rollbackOutcome = await RollbackAsync(journal);
	await FinalizeFailedAsync(taskStore, options, startTime, safeMessage, journal, logAppender, rollbackOutcome, modelId: modelClient?.ModelId);
	return 1;
}

static async Task<bool> RollbackAsync(WriteJournal journal)
{
	using var rollbackSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.rollback");
	try
	{
		var outcomes = await journal.RollbackAsync(CancellationToken.None);
		var allOk = outcomes.Values.All(ok => ok);
		IngestAgentMetrics.RecordRollback(allOk);
		rollbackSpan?.SetTag("paths_restored", outcomes.Count);
		return allOk;
	}
	catch
	{
		IngestAgentMetrics.RecordRollback(false);
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
	LoadedInstructionSet? instructionSet = null,
	LoadedPolicy? policy = null,
	string? modelId = null)
{
	using var finalizeSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.finalize_artifact");
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
			DeniedActions: [],
			InstructionFiles: instructionSet?.Files.Select(f => new InstructionFileRecord(f.Path, f.Sha256)).ToList(),
			Policy: policy is null ? null : new PolicyRecord(policy.Identity.Path, policy.Identity.Version, policy.Identity.Sha256),
			Model: modelId,
			Turns: null,
			RolledBack: journal is not null ? rolledBack : null),
		CancellationToken.None);

	await logAppender.EnsureLogEntryAsync(
		options.LogPath, "failed", options.SourceRef, options.TaskId,
		forceAppend: true, CancellationToken.None);

	IngestAgentMetrics.RecordIngest("failed", 0, "none",
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

	return new AgentCliOptions(
		TaskId: GetRequired("--task-id"),
		SourceRef: GetRequired("--source-ref"),
		SourceKind: sourceKind,
		PagesDir: GetRequired("--pages-dir"),
		TasksDir: GetRequired("--tasks-dir"),
		IndexPath: GetRequired("--index-path"),
		LogPath: GetRequired("--log-path"),
		PastedText: pastedText,
		InstructionsDir: GetRequired("--instructions-dir"),
		PolicyPath: GetRequired("--policy-path"));
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
