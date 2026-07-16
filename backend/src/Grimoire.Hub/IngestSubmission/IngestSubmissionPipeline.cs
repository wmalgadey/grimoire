using System.Diagnostics;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.Conversion;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.TaskArtifact;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Hub.IngestSubmission;

/// <summary>
/// One accepted source submission: URL, or a single uploaded file
/// (data-model.md IngestSubmission), optionally carrying a custom steering prompt and
/// per-step convert overrides (004 FR-006, FR-011). The caller validates before
/// constructing this input.
/// </summary>
public sealed record IngestSubmissionInput(
    IngestSubmissionKind Kind,
    string? Url,
    string? FileName,
    byte[]? FileBytes,
    string? FileContentType,
    string? UserPrompt = null,
    IReadOnlyDictionary<string, bool>? ConvertSteps = null);

/// <summary>
/// Orchestrates the ingest-submission phase end to end (FR-002, FR-004, FR-005, FR-006, FR-010):
/// creates the Task Artifact at `received`, drives it through `converting` (fetch/convert +
/// persist artifacts) to `queued` or `failed`, then hands the task to the
/// <see cref="IngestRunCoordinator"/> — non-blocking, queue-driven, event-supervised
/// (ADR-008; 004 FR-016/FR-019). Every stage transition is published to the board over
/// <see cref="IngestLifecyclePublisher"/>.
/// </summary>
public sealed class IngestSubmissionPipeline
{
    private readonly HubTaskArtifactWriter _taskArtifactWriter;
    private readonly SourceArtifactStore _sourceArtifactStore;
    private readonly MarkItDownConverter _converter;
    private readonly UrlContentFetcher _urlFetcher;
    private readonly IngestLifecyclePublisher _publisher;
    private readonly IngestRunCoordinator _coordinator;
    private readonly ContentRootPaths _contentPaths;
    private readonly ILogger<IngestSubmissionPipeline> _logger;

    public IngestSubmissionPipeline(
        HubTaskArtifactWriter taskArtifactWriter,
        SourceArtifactStore sourceArtifactStore,
        MarkItDownConverter converter,
        UrlContentFetcher urlFetcher,
        IngestLifecyclePublisher publisher,
        IngestRunCoordinator coordinator,
        ContentRootPaths contentPaths,
        ILogger<IngestSubmissionPipeline>? logger = null)
    {
        _taskArtifactWriter = taskArtifactWriter;
        _sourceArtifactStore = sourceArtifactStore;
        _converter = converter;
        _urlFetcher = urlFetcher;
        _publisher = publisher;
        _coordinator = coordinator;
        _contentPaths = contentPaths;
        _logger = logger ?? NullLogger<IngestSubmissionPipeline>.Instance;
    }

    /// <summary>
    /// Validates nothing itself (the caller validates first, FR-001/FR-003) — creates the Task
    /// Artifact at `received` and returns immediately (FR-002); the rest of the pipeline runs in
    /// the background so one submission's conversion never blocks another's acceptance (FR-012).
    /// </summary>
    public async Task<string> AcceptAsync(IngestSubmissionInput input, CancellationToken cancellationToken = default)
    {
        using var submitSpan = HubTracing.ActivitySource.StartActivity("hub.ingest_submission.submit");
        var taskId = GenerateTaskId();
        submitSpan?.SetTag("task_id", taskId);
        submitSpan?.SetTag("source_kind", KindLabel(input.Kind));

        var taskArtifactPath = Path.Combine(_contentPaths.TasksDir, $"{taskId}.md");
        var submittedAt = DateTimeOffset.UtcNow;

        var promptSource = input.UserPrompt is null ? "default" : "custom";
        var effectiveSteps = ConvertStepRegistry.ResolveEffective(KindLabel(input.Kind), input.ConvertSteps);
        var context = new PipelineContext(taskId, taskArtifactPath, submittedAt, promptSource, input.UserPrompt, effectiveSteps);

        await WriteStageAsync(context, "received", null, null, null,
            $"Ingest submission received ({KindLabel(input.Kind)}).", cancellationToken);
        await _publisher.PublishAsync(taskId, null, "received", cancellationToken: cancellationToken);

        HubMetrics.RecordIngestSubmission(KindLabel(input.Kind), "accepted");
        IngestSubmissionLogEvents.LogSubmissionAccepted(_logger, taskId, KindLabel(input.Kind), submittedAt);

        // 004 prompt/convert configuration is recorded at acceptance (SC-003).
        HubMetrics.RecordUserPrompt(promptSource);
        IngestSubmissionLogEvents.LogPromptConfig(_logger, taskId, promptSource, input.UserPrompt?.Length ?? 0);
        foreach (var (step, enabled) in effectiveSteps)
        {
            IngestSubmissionLogEvents.LogConvertConfig(_logger, taskId, step, enabled);
            if (!enabled)
            {
                HubMetrics.RecordConvertStepDisabled(step);
            }
        }

        // Fire-and-forget: the HTTP response returns now; ExecutionContext flow keeps this task's
        // spans correctly parented under hub.ingest_submission.submit even after it disposes.
        _ = Task.Run(() => ProcessAsync(context, input, CancellationToken.None), CancellationToken.None);

        return taskId;
    }

    private async Task ProcessAsync(PipelineContext context, IngestSubmissionInput input, CancellationToken cancellationToken)
    {
        var taskId = context.TaskId;
        try
        {
            await WriteStageAsync(context, "converting", null, null, null,
                "Converting submitted source to Markdown.", cancellationToken);
            await _publisher.PublishAsync(taskId, "received", "converting", cancellationToken: cancellationToken);

            var conversionStarted = Stopwatch.GetTimestamp();
            var markItDownEnabled = ApplyConvertConfig(context, ConvertStepRegistry.MarkItDown);

            string originalPath;
            string originalContentType;
            long originalSize;
            SourceArtifactSet artifactSet;

            if (input.Kind == IngestSubmissionKind.Url)
            {
                UrlFetchResult fetchResult;
                using (var fetchSpan = HubTracing.ActivitySource.StartActivity("hub.ingest_submission.fetch_url"))
                {
                    fetchSpan?.SetTag("task_id", taskId);
                    fetchSpan?.SetTag("url_host", TryGetHost(input.Url));

                    fetchResult = await _urlFetcher.FetchAsync(new Uri(input.Url!), cancellationToken);
                    fetchSpan?.SetTag("http_status", fetchResult.HttpStatus);
                }

                HubMetrics.RecordIngestSubmissionUrlFetch(fetchResult.Success ? "completed" : "failed", fetchResult.Success ? null : "fetch_error");

                if (!fetchResult.Success)
                {
                    IngestSubmissionLogEvents.LogUrlFetchFailed(_logger, taskId, input.Url!, fetchResult.FailureReason!, fetchResult.HttpStatus);
                    await FailAsync(context, fetchResult.FailureReason!, cancellationToken);
                    return;
                }

                var extension = ExtensionFromContentType(fetchResult.ContentType);
                originalContentType = fetchResult.ContentType ?? "application/octet-stream";
                originalPath = await PersistOriginalAsync(taskId, extension, fetchResult.Content!, originalContentType, cancellationToken);
                originalSize = fetchResult.Content!.LongLength;

                if (markItDownEnabled)
                {
                    var conversion = await ConvertAsync(context, input.Kind, originalPath, cancellationToken);
                    if (conversion is null)
                    {
                        return;
                    }
                    artifactSet = await PersistNormalizedAsync(taskId, originalPath, originalContentType, originalSize, conversion, cancellationToken);
                }
                else
                {
                    // Convert step disabled (FR-012): store the fetched content exactly as
                    // received — byte-identical, checksum over the unmodified bytes (SC-004).
                    artifactSet = await PersistNormalizedBytesAsync(taskId, originalPath, originalContentType, originalSize, fetchResult.Content!, cancellationToken);
                }
            }
            else if (input.Kind == IngestSubmissionKind.MarkdownFile)
            {
                var extension = Path.GetExtension(input.FileName) is { Length: > 0 } ext ? ext : ".md";
                originalContentType = input.FileContentType ?? "text/markdown";
                originalPath = await PersistOriginalAsync(taskId, extension, input.FileBytes!, originalContentType, cancellationToken);
                originalSize = input.FileBytes!.LongLength;
                // Markdown is already the canonical format: pass through byte-identical,
                // never routed through MarkItDown (FR-004; 004 FR-015).
                artifactSet = await PersistNormalizedBytesAsync(taskId, originalPath, originalContentType, originalSize, input.FileBytes!, cancellationToken);
            }
            else
            {
                // PDF/Office: the convert step is required (FR-013) — the validator rejected
                // any disabled configuration before a task was created.
                var extension = Path.GetExtension(input.FileName) ?? string.Empty;
                originalContentType = input.FileContentType ?? "application/octet-stream";
                originalPath = await PersistOriginalAsync(taskId, extension, input.FileBytes!, originalContentType, cancellationToken);
                originalSize = input.FileBytes!.LongLength;

                var conversion = await ConvertAsync(context, input.Kind, originalPath, cancellationToken);
                if (conversion is null)
                {
                    return;
                }
                artifactSet = await PersistNormalizedAsync(taskId, originalPath, originalContentType, originalSize, conversion, cancellationToken);
            }

            HubMetrics.RecordIngestSubmissionArtifactPersisted("normalized_markdown");
            HubMetrics.RecordIngestSubmissionConversion(KindLabel(input.Kind), "completed");
            var durationMs = (long)Stopwatch.GetElapsedTime(conversionStarted).TotalMilliseconds;
            IngestSubmissionLogEvents.LogConversionCompleted(_logger, taskId, KindLabel(input.Kind), artifactSet.NormalizedMarkdownPath, durationMs);

            await WriteStageAsync(context, "queued", artifactSet.NormalizedMarkdownPath, artifactSet.OriginalPath, null,
                "Queued for ingest.", cancellationToken);
            await _publisher.PublishAsync(taskId, "converting", "queued", cancellationToken: cancellationToken);

            // Non-blocking hand-off (ADR-008): the coordinator owns queueing, the single
            // agent slot, event supervision, and terminal transitions from here on.
            await _coordinator.EnqueueAsync(taskId, artifactSet.NormalizedMarkdownPath, context.UserPrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            await FailAsync(context, $"Unexpected ingest-submission error: {ex.Message}", cancellationToken);
        }
    }

    private bool ApplyConvertConfig(PipelineContext context, string stepName)
    {
        if (!context.ConvertSteps.TryGetValue(stepName, out var enabled))
        {
            // Step not applicable to this kind (e.g. markdown_file): nothing to apply.
            return true;
        }

        using var span = HubTracing.ActivitySource.StartActivity("ingest_submission.apply_convert_config");
        span?.SetTag("task_id", context.TaskId);
        span?.SetTag("step", stepName);
        span?.SetTag("enabled", enabled);
        return enabled;
    }

    private async Task<string?> ConvertAsync(PipelineContext context, IngestSubmissionKind kind, string originalPath, CancellationToken cancellationToken)
    {
        using var convertSpan = HubTracing.ActivitySource.StartActivity("hub.ingest_submission.convert_to_markdown");
        convertSpan?.SetTag("task_id", context.TaskId);
        convertSpan?.SetTag("source_kind", KindLabel(kind));
        convertSpan?.SetTag("converter", "markitdown");

        var conversion = await _converter.ConvertAsync(originalPath, cancellationToken);
        if (!conversion.Success)
        {
            HubMetrics.RecordIngestSubmissionConversion(KindLabel(kind), "failed");
            IngestSubmissionLogEvents.LogConversionFailed(_logger, context.TaskId, KindLabel(kind), conversion.FailureReason!);
            await FailAsync(context, conversion.FailureReason!, cancellationToken);
            return null;
        }

        return conversion.Markdown;
    }

    private async Task<SourceArtifactSet> PersistNormalizedAsync(
        string taskId, string originalPath, string originalContentType, long originalSize, string normalizedMarkdown, CancellationToken cancellationToken)
    {
        using var storeNormalizedSpan = HubTracing.ActivitySource.StartActivity("hub.ingest_submission.store_normalized");
        storeNormalizedSpan?.SetTag("task_id", taskId);

        var artifactSet = await _sourceArtifactStore.PersistNormalizedAsync(
            taskId, originalPath, originalContentType, originalSize, normalizedMarkdown, cancellationToken);
        storeNormalizedSpan?.SetTag("normalized_path", artifactSet.NormalizedMarkdownPath);
        return artifactSet;
    }

    private async Task<SourceArtifactSet> PersistNormalizedBytesAsync(
        string taskId, string originalPath, string originalContentType, long originalSize, byte[] normalizedBytes, CancellationToken cancellationToken)
    {
        using var storeNormalizedSpan = HubTracing.ActivitySource.StartActivity("hub.ingest_submission.store_normalized");
        storeNormalizedSpan?.SetTag("task_id", taskId);

        var artifactSet = await _sourceArtifactStore.PersistNormalizedBytesAsync(
            taskId, originalPath, originalContentType, originalSize, normalizedBytes, cancellationToken);
        storeNormalizedSpan?.SetTag("normalized_path", artifactSet.NormalizedMarkdownPath);
        return artifactSet;
    }

    private async Task<string> PersistOriginalAsync(string taskId, string extension, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        using var storeOriginalSpan = HubTracing.ActivitySource.StartActivity("hub.ingest_submission.store_original");
        storeOriginalSpan?.SetTag("task_id", taskId);

        var originalPath = await _sourceArtifactStore.PersistOriginalAsync(taskId, extension, bytes, cancellationToken);
        storeOriginalSpan?.SetTag("original_path", originalPath);
        storeOriginalSpan?.SetTag("size_bytes", bytes.LongLength);

        HubMetrics.RecordIngestSubmissionArtifactPersisted("original");
        IngestSubmissionLogEvents.LogOriginalPersisted(_logger, taskId, originalPath, bytes.LongLength, contentType);
        return originalPath;
    }

    private async Task FailAsync(PipelineContext context, string failureReason, CancellationToken cancellationToken)
    {
        _sourceArtifactStore.DeletePartialNormalizedArtifact(context.TaskId);
        await WriteStageAsync(context, "failed", null, null, failureReason, failureReason, cancellationToken);
        await _publisher.PublishAsync(context.TaskId, null, "failed", failureReason, cancellationToken);
    }

    private async Task WriteStageAsync(
        PipelineContext context, string status, string? sourceRef, string? originalRef, string? failureReason, string narrative, CancellationToken cancellationToken)
    {
        var document = new HubTaskArtifactDocument(
            TaskId: context.TaskId,
            Status: status,
            StartedAt: context.SubmittedAt,
            CompletedAt: status is "completed" or "failed" ? DateTimeOffset.UtcNow : null,
            SourceRef: sourceRef,
            OriginalRef: originalRef,
            FailureReason: failureReason,
            Narrative: narrative,
            UserPromptSource: context.PromptSource,
            UserPrompt: context.UserPrompt,
            ConvertSteps: context.ConvertSteps);

        await _taskArtifactWriter.WriteAsync(context.TaskArtifactPath, document, cancellationToken);
    }

    private static string GenerateTaskId() => $"{DateTime.UtcNow:yyyy-MM-dd}-ingest-{Guid.NewGuid():N}";

    private static string KindLabel(IngestSubmissionKind kind) => kind switch
    {
        IngestSubmissionKind.Url => "url",
        IngestSubmissionKind.MarkdownFile => "markdown_file",
        IngestSubmissionKind.PdfFile => "pdf_file",
        IngestSubmissionKind.OfficeFile => "office_file",
        _ => "unknown",
    };

    private static string ExtensionFromContentType(string? contentType) => contentType switch
    {
        "text/html" => ".html",
        "text/plain" => ".txt",
        "application/pdf" => ".pdf",
        _ => ".html",
    };

    private static string? TryGetHost(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

    private sealed record PipelineContext(
        string TaskId,
        string TaskArtifactPath,
        DateTimeOffset SubmittedAt,
        string PromptSource,
        string? UserPrompt,
        IReadOnlyDictionary<string, bool> ConvertSteps);
}
