using Grimoire.Domain.Ingest;
using Grimoire.IngestAgent.Guardrails;
using Grimoire.IngestAgent;
using Grimoire.IngestAgent.IngestLog;
using Grimoire.IngestAgent.Instructions;
using Grimoire.IngestAgent.Source;
using Grimoire.IngestAgent.Synthesis;
using Grimoire.IngestAgent.TaskArtifact;
using Grimoire.IngestAgent.WikiIndex;
using Grimoire.IngestAgent.WikiWrite;
using System.Text.RegularExpressions;

using var telemetry = TelemetryBootstrap.Build();

var options = ParseArgs(args);
var taskStore = new TaskArtifactStore();
var logAppender = new IngestLogAppender();

GuardrailPolicy policy;
try
{
	policy = await new GuardrailPolicyLoader().LoadAsync(options.GuardrailPolicyPath, CancellationToken.None);
}
catch (Exception ex)
{
	Console.Error.WriteLine($"Guardrail policy load failed: {ex.Message}");
	return 3;
}

var guardrailEvaluator = new GuardrailEvaluator(policy);
var guardedFileOperations = new GuardedFileOperations(options.InstructionsRoot, guardrailEvaluator);
InstructionContextSnapshot instructionSnapshot;
using (var instructionSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.instructions.load"))
{
	instructionSnapshot = await new InstructionContextLoader().LoadAsync(options, CancellationToken.None);
	instructionSpan?.SetTag("task_id", options.TaskId);
	instructionSpan?.SetTag("claude_path", instructionSnapshot.ClaudePath);
	instructionSpan?.SetTag("skill_path_count", instructionSnapshot.SkillPaths.Count);
	instructionSpan?.SetTag("status", instructionSnapshot.Status);
}
IngestAgentMetrics.RecordInstructionLoad(options.TaskId, instructionSnapshot.Status);

var startTime = DateTimeOffset.UtcNow;
using var processSourceSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.process_source");
processSourceSpan?.SetTag("task_id", options.TaskId);

var rollbackSnapshots = new Dictionary<string, string?>(StringComparer.Ordinal);

try
{
	await taskStore.WriteAsync(
		options.TaskArtifactPath,
		new TaskArtifactDocument(
			options.TaskId,
			"ingest",
			"running",
			DateTimeOffset.UtcNow,
			null,
			options.SourceRef,
			[],
			[],
			[],
			[],
			[],
			"Ingest started and source is being processed.",
			null,
			InstructionContext: new InstructionContextRecord(
				instructionSnapshot.ClaudePath,
				instructionSnapshot.SkillPaths,
				instructionSnapshot.ContentHash)),
		CancellationToken.None);

	var sourceReader = new SourceReader();
	var readSource = await sourceReader.ReadAsync(options.SourceKind, options.SourceRef, options.PastedText, CancellationToken.None);

	var indexMarkdown = File.Exists(options.IndexPath) ? await File.ReadAllTextAsync(options.IndexPath) : string.Empty;
	var synthesis = await new ClaudeSynthesisService().SynthesizeAsync(readSource.Content, CancellationToken.None);

	var planner = new WikiStructurePlanner(new UpdateOrCreateDecisionService(), new WikiFrontmatterBuilder());
	IReadOnlyList<PlannedWikiAction> plannedActions;
	using (var planSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.plan_wiki_structure"))
	{
		plannedActions = planner.BuildPlan(synthesis, indexMarkdown);
		planSpan?.SetTag("task_id", options.TaskId);
		planSpan?.SetTag("candidate_pages", plannedActions.Count);
	}

	var writer = new WikiPageWriter();
	foreach (var planned in plannedActions)
	{
		var fullPath = writer.ResolvePath(options.PagesDir, planned.RelativePath);
		rollbackSnapshots[fullPath] = File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath) : null;
	}

	var appliedActions = await writer.ApplyPlannedWritesAsync(options.PagesDir, plannedActions, guardedFileOperations, options.DryRun, CancellationToken.None);

	var indexEntries = appliedActions
		.Where(x => x.Applied)
		.Select(x => new IndexUpdateEntry(x.Kind, x.Category, x.Title, x.RelativePath, x.Summary))
		.ToList();

	var indexWriter = new WikiIndexWriter();
	if (indexEntries.Count > 0)
	{
		await indexWriter.UpdateFromActionsAsync(options.IndexPath, indexEntries, CancellationToken.None);
	}

	var deniedActions = guardedFileOperations.DeniedActions.Select(x => new DeniedActionRecord(x.Action, x.TargetPath, x.Reason)).ToList();
	foreach (var denied in guardedFileOperations.DeniedActions)
	{
		IngestAgentMetrics.RecordGuardrailDecision(options.TaskId, denied.Action, false, denied.RuleId);
		await logAppender.AppendDeniedActionAsync(
			options.LogPath,
			options.TaskId,
			new IngestLogAppender.DeniedLogEntry(denied.Action, denied.TargetPath, denied.Reason, denied.RuleId),
			CancellationToken.None);
	}

	var createdPaths = appliedActions.Where(x => x.Applied && x.Action == "create").Select(x => x.RelativePath).ToList();
	var updatedPaths = appliedActions.Where(x => x.Applied && x.Action == "update").Select(x => x.RelativePath).ToList();
	var supersededPaths = plannedActions.SelectMany(x => x.Supersedes).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

	foreach (var entry in indexEntries)
	{
		IngestAgentMetrics.RecordGuardrailDecision(options.TaskId, "write", true);
	}

	await logAppender.AppendCompletionSummaryAsync(
		options.LogPath,
		options.TaskId,
		createdPaths.Count,
		updatedPaths.Count,
		supersededPaths.Count,
		deniedActions.Count,
		CancellationToken.None);

	var completionDetail = $"created={createdPaths.Count}, updated={updatedPaths.Count}, denied={deniedActions.Count}";
	await logAppender.AppendAsync(options.LogPath, "completed", options.SourceRef, completionDetail, options.TaskId, CancellationToken.None);

	await taskStore.WriteAsync(
		options.TaskArtifactPath,
		new TaskArtifactDocument(
			options.TaskId,
			"ingest",
			"completed",
			(await taskStore.ReadAsync(options.TaskArtifactPath, CancellationToken.None)).StartedAt,
			DateTimeOffset.UtcNow,
			options.SourceRef,
			createdPaths,
			updatedPaths,
			supersededPaths,
			deniedActions,
			[],
			$"Completed ingest with {createdPaths.Count} created pages, {updatedPaths.Count} updated pages, and {deniedActions.Count} denied actions.",
			null,
			InstructionContext: new InstructionContextRecord(
				instructionSnapshot.ClaudePath,
				instructionSnapshot.SkillPaths,
				instructionSnapshot.ContentHash)),
		CancellationToken.None);

	var pagesTouched = createdPaths.Count + updatedPaths.Count;
	var pageAction = createdPaths.Count > 0 && updatedPaths.Count > 0
		? "mixed"
		: createdPaths.Count > 0 ? "created" : "updated";
	IngestAgentMetrics.RecordIngest("completed", pagesTouched, pageAction, (DateTimeOffset.UtcNow - startTime).TotalSeconds);
	IngestAgentMetrics.RecordSupersededPages(options.TaskId, supersededPaths.Count);

	Console.WriteLine($"Ingest summary: created={createdPaths.Count}, updated={updatedPaths.Count}, superseded={supersededPaths.Count}, denied={deniedActions.Count}.");
	return 0;
}
catch (Exception ex)
{
	var safeMessage = SanitizeErrorText(ex.Message);
	var safeExceptionDetails = SanitizeErrorText(ex.ToString());

	// Attempt to roll back wiki writes to preserve pre-ingest state (FR-008).
	foreach (var rollbackEntry in rollbackSnapshots)
	{
		try
		{
			if (rollbackEntry.Value is not null)
			{
				await File.WriteAllTextAsync(rollbackEntry.Key, rollbackEntry.Value);
			}
			else if (File.Exists(rollbackEntry.Key))
			{
				File.Delete(rollbackEntry.Key);
			}
		}
		catch { /* rollback is best-effort; task artifact records the failure */ }
	}

	var startedAt = DateTimeOffset.UtcNow;
	try { startedAt = (await taskStore.ReadAsync(options.TaskArtifactPath, CancellationToken.None)).StartedAt; }
	catch { /* running artifact may not exist if the exception occurred before it was written */ }

	await taskStore.WriteAsync(
		options.TaskArtifactPath,
		new TaskArtifactDocument(
			options.TaskId,
			"ingest",
			"failed",
			startedAt,
			DateTimeOffset.UtcNow,
			options.SourceRef,
			[],
			[],
			[],
			guardedFileOperations.DeniedActions.Select(x => new DeniedActionRecord(x.Action, x.TargetPath, x.Reason)).ToList(),
			[],
			$"Ingest failed: {safeMessage}",
			safeMessage,
			InstructionContext: new InstructionContextRecord(
				instructionSnapshot.ClaudePath,
				instructionSnapshot.SkillPaths,
				instructionSnapshot.ContentHash)),
		CancellationToken.None);

	await logAppender.AppendAsync(options.LogPath, "failed", options.SourceRef, $"error: {safeMessage}", options.TaskId, CancellationToken.None);
	IngestAgentMetrics.RecordIngest("failed", 0, "none", (DateTimeOffset.UtcNow - startTime).TotalSeconds);
	return 1;
}

static string SanitizeErrorText(string message)
{
	if (string.IsNullOrWhiteSpace(message))
	{
		return "Unknown ingest error.";
	}

	var sanitized = message;
	var envAuthToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
	if (!string.IsNullOrWhiteSpace(envAuthToken))
	{
		sanitized = sanitized.Replace(envAuthToken, "[REDACTED]", StringComparison.Ordinal);
	}

	// Redact common Anthropic API key token shape if present in exception text.
	sanitized = Regex.Replace(sanitized, "sk-ant-[A-Za-z0-9_-]+", "[REDACTED]", RegexOptions.CultureInvariant);
	return sanitized;
}

static AgentCliOptions ParseArgs(string[] args)
{
	var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	var skillPaths = new List<string>();

	for (var i = 0; i < args.Length; i++)
	{
		if (!args[i].StartsWith("--", StringComparison.Ordinal))
		{
			continue;
		}

		if (string.Equals(args[i], "--skill-path", StringComparison.OrdinalIgnoreCase))
		{
			if (i + 1 >= args.Length)
			{
				throw new ArgumentException("Missing value for --skill-path");
			}

			skillPaths.Add(args[++i]);
			continue;
		}

		if (string.Equals(args[i], "--dry-run", StringComparison.OrdinalIgnoreCase))
		{
			options[args[i]] = "true";
			continue;
		}

		if (i + 1 >= args.Length)
		{
			throw new ArgumentException($"Missing value for {args[i]}");
		}

		options[args[i]] = args[++i];
	}

	string GetRequired(string name)
		=> options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
			? value
			: throw new ArgumentException($"Missing required argument {name}");

	var sourceKind = GetRequired("--source-kind");
	string? pastedText = null;
	if (sourceKind == "pasted_text")
	{
		pastedText = Console.In.ReadToEnd();
	}

	return new AgentCliOptions(
		TaskId: GetRequired("--task-id"),
		SourceRef: GetRequired("--source-ref"),
		SourceKind: sourceKind,
		PagesDir: GetRequired("--pages-dir"),
		TasksDir: GetRequired("--tasks-dir"),
		IndexPath: GetRequired("--index-path"),
		LogPath: GetRequired("--log-path"),
		GuardrailPolicyPath: GetRequired("--guardrail-policy-path"),
		InstructionsRoot: GetRequired("--instructions-root"),
		SkillPaths: skillPaths,
		SkillName: options.TryGetValue("--skill-name", out var skillName) ? skillName : null,
		DryRun: options.TryGetValue("--dry-run", out var dryRun) && bool.TryParse(dryRun, out var parsedDryRun) && parsedDryRun,
		PastedText: pastedText);
}
