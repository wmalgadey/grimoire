using Grimoire.Domain.Ingest;
using Grimoire.IngestAgent;
using Grimoire.IngestAgent.IngestLog;
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

var startTime = DateTimeOffset.UtcNow;
using var processSourceSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.process_source");
processSourceSpan?.SetTag("task_id", options.TaskId);

// Track the wiki page path and original content for rollback on failure (FR-008).
string? wikiRollbackPath = null;
string? wikiRollbackContent = null;

try
{
	await taskStore.WriteAsync(
		options.TaskArtifactPath,
		new TaskArtifactDocument(
			options.TaskId,
			"ingest",
			"running",
			"ingest",
			DateTimeOffset.UtcNow,
			null,
			options.SourceRef,
			[],
			null,
			"Ingest started and source is being processed."),
		CancellationToken.None);

	var sourceReader = new SourceReader();
	var readSource = await sourceReader.ReadAsync(options.SourceKind, options.SourceRef, options.PastedText, CancellationToken.None);

	var indexMarkdown = File.Exists(options.IndexPath) ? await File.ReadAllTextAsync(options.IndexPath) : string.Empty;
	var synthesis = await new ClaudeSynthesisService().SynthesizeAsync(readSource.Content, CancellationToken.None);

	var decisionService = new UpdateOrCreateDecisionService();
	PageDecision decision;
	using (var decideSpan = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.decide_page_target"))
	{
		decision = decisionService.Decide(synthesis.Title, indexMarkdown);
		decideSpan?.SetTag("task_id", options.TaskId);
		decideSpan?.SetTag("decision", decision.Action.ToString().ToLower());
	}

	var writer = new WikiPageWriter();

	// Read existing page content before overwriting so we can roll back on failure (FR-008).
	wikiRollbackPath = writer.ResolvePath(options.PagesDir, decision.TargetPagePath);
	wikiRollbackContent = File.Exists(wikiRollbackPath) ? await File.ReadAllTextAsync(wikiRollbackPath) : null;

	var wikiFullPath = await writer.WriteAsync(options.PagesDir, decision.TargetPagePath, synthesis.Content, CancellationToken.None);
	var wikiRelativePath = Path.GetRelativePath(Path.GetDirectoryName(options.IndexPath) ?? options.PagesDir, wikiFullPath).Replace('\\', '/');

	var indexWriter = new WikiIndexWriter();
	await indexWriter.UpdateAsync(options.IndexPath, synthesis.Category, synthesis.Title, wikiRelativePath, synthesis.Summary, CancellationToken.None);

	await logAppender.AppendAsync(options.LogPath, "completed", options.SourceRef, $"{decision.Action} {wikiRelativePath}", options.TaskId, CancellationToken.None);

	await taskStore.WriteAsync(
		options.TaskArtifactPath,
		new TaskArtifactDocument(
			options.TaskId,
			"ingest",
			"completed",
			"ingest",
			(await taskStore.ReadAsync(options.TaskArtifactPath, CancellationToken.None)).StartedAt,
			DateTimeOffset.UtcNow,
			options.SourceRef,
			[wikiRelativePath],
			null,
			$"Completed ingest. Page action: {decision.Action}. Updated {wikiRelativePath}."),
		CancellationToken.None);

	var pageAction = decision.Action == PageDecisionAction.Update ? "updated" : "created";
	IngestAgentMetrics.RecordIngest("completed", 1, pageAction, (DateTimeOffset.UtcNow - startTime).TotalSeconds);
	return 0;
}
catch (Exception ex)
{
	var safeMessage = SanitizeErrorText(ex.Message);
	var safeExceptionDetails = SanitizeErrorText(ex.ToString());

	// Attempt to roll back the wiki page write to preserve pre-ingest state (FR-008).
	if (wikiRollbackPath is not null)
	{
		try
		{
			if (wikiRollbackContent is not null)
			{
				await File.WriteAllTextAsync(wikiRollbackPath, wikiRollbackContent);
			}
			else if (File.Exists(wikiRollbackPath))
			{
				File.Delete(wikiRollbackPath);
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
			"ingest",
			startedAt,
			DateTimeOffset.UtcNow,
			options.SourceRef,
			[],
			safeMessage,
			$"Ingest failed: {safeMessage}\n\n```text\n{safeExceptionDetails}\n```"),
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

	for (var i = 0; i < args.Length - 1; i += 2)
	{
		if (args[i].StartsWith("--", StringComparison.Ordinal))
		{
			options[args[i]] = args[i + 1];
		}
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
		PastedText: pastedText);
}
