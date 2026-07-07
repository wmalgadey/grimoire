using System.Diagnostics;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.Conversion;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.TaskArtifact;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Hub.IngestSubmission;

/// <summary>
/// One accepted source submission: URL, or a single uploaded file
/// (data-model.md IngestSubmission).
/// </summary>
public sealed record IngestSubmissionInput(
    IngestSubmissionKind Kind,
    string? Url,
    string? FileName,
    byte[]? FileBytes,
    string? FileContentType);

/// <summary>
/// Orchestrates the ingest-submission phase end to end (FR-002, FR-004, FR-005, FR-006, FR-010):
/// creates the Task Artifact at `received`, drives it through `converting` (fetch/convert +
/// persist artifacts) to `queued` or `failed`, then auto-triggers the Ingest agent once queued,
/// respecting the single-concurrent-run constraint (FR-013). Every stage transition is published
/// to the board over <see cref="IngestLifecyclePublisher"/>.
/// </summary>
public sealed class IngestSubmissionPipeline
{
    private readonly HubTaskArtifactWriter _taskArtifactWriter;
    private readonly SourceArtifactStore _sourceArtifactStore;
    private readonly MarkItDownConverter _converter;
    private readonly UrlContentFetcher _urlFetcher;
    private readonly IngestLifecyclePublisher _publisher;
    private readonly IIngestAgentDispatcher _dispatcher;
    private readonly IngestRunGate _runGate;
    private readonly OperationalStateRepository _repository;
    private readonly ContentRootPaths _contentPaths;
    private readonly ILogger<IngestSubmissionPipeline> _logger;

    public IngestSubmissionPipeline(
        HubTaskArtifactWriter taskArtifactWriter,
        SourceArtifactStore sourceArtifactStore,
        MarkItDownConverter converter,
        UrlContentFetcher urlFetcher,
        IngestLifecyclePublisher publisher,
        IIngestAgentDispatcher dispatcher,
        IngestRunGate runGate,
        OperationalStateRepository repository,
        ContentRootPaths contentPaths,
        ILogger<IngestSubmissionPipeline>? logger = null)
    {
        _taskArtifactWriter = taskArtifactWriter;
        _sourceArtifactStore = sourceArtifactStore;
        _converter = converter;
        _urlFetcher = urlFetcher;
        _publisher = publisher;
        _dispatcher = dispatcher;
        _runGate = runGate;
        _repository = repository;
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

        var context = new PipelineContext(taskId, taskArtifactPath, submittedAt);

        await WriteStageAsync(context, "received", null, null, null,
            $"Ingest submission received ({KindLabel(input.Kind)}).", cancellationToken);
        await _publisher.PublishAsync(taskId, null, "received", cancellationToken: cancellationToken);

        HubMetrics.RecordIngestSubmission(KindLabel(input.Kind), "accepted");
        IngestSubmissionLogEvents.LogSubmissionAccepted(_logger, taskId, KindLabel(input.Kind), submittedAt);

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
            string originalPath;
            string originalContentType;
            long originalSize;
            string normalizedMarkdown;

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
                originalPath = await PersistOriginalAsync(taskId, extension, fetchResult.Content!, cancellationToken);
                originalContentType = fetchResult.ContentType ?? "application/octet-stream";
                originalSize = fetchResult.Content!.LongLength;

                var conversion = await ConvertAsync(context, input.Kind, originalPath, cancellationToken);
                if (conversion is null)
                {
                    return;
                }
                normalizedMarkdown = conversion;
            }
            else if (input.Kind == IngestSubmissionKind.MarkdownFile)
            {
                var extension = Path.GetExtension(input.FileName) is { Length: > 0 } ext ? ext : ".md";
                originalPath = await PersistOriginalAsync(taskId, extension, input.FileBytes!, cancellationToken);
                originalContentType = input.FileContentType ?? "text/markdown";
                originalSize = input.FileBytes!.LongLength;
                // Markdown is already the canonical format: pass through, never routed through MarkItDown (FR-004).
                normalizedMarkdown = System.Text.Encoding.UTF8.GetString(input.FileBytes!);
            }
            else
            {
                var extension = Path.GetExtension(input.FileName) ?? string.Empty;
                originalPath = await PersistOriginalAsync(taskId, extension, input.FileBytes!, cancellationToken);
                originalContentType = input.FileContentType ?? "application/octet-stream";
                originalSize = input.FileBytes!.LongLength;

                var conversion = await ConvertAsync(context, input.Kind, originalPath, cancellationToken);
                if (conversion is null)
                {
                    return;
                }
                normalizedMarkdown = conversion;
            }

            SourceArtifactSet artifactSet;
            using (var storeNormalizedSpan = HubTracing.ActivitySource.StartActivity("hub.ingest_submission.store_normalized"))
            {
                storeNormalizedSpan?.SetTag("task_id", taskId);

                artifactSet = await _sourceArtifactStore.PersistNormalizedAsync(
                    taskId, originalPath, originalContentType, originalSize, normalizedMarkdown, cancellationToken);
                storeNormalizedSpan?.SetTag("normalized_path", artifactSet.NormalizedMarkdownPath);
            }

            HubMetrics.RecordIngestSubmissionArtifactPersisted("normalized_markdown");
            HubMetrics.RecordIngestSubmissionConversion(KindLabel(input.Kind), "completed");
            var durationMs = Stopwatch.GetElapsedTime(conversionStarted).Milliseconds;
            IngestSubmissionLogEvents.LogConversionCompleted(_logger, taskId, KindLabel(input.Kind), artifactSet.NormalizedMarkdownPath, durationMs);

            var queuedAt = DateTimeOffset.UtcNow;
            await WriteStageAsync(context, "queued", artifactSet.NormalizedMarkdownPath, artifactSet.OriginalPath, null,
                "Queued for ingest.", cancellationToken);
            await _publisher.PublishAsync(taskId, "converting", "queued", cancellationToken: cancellationToken);

            _ = TriggerAsync(context, artifactSet, queuedAt, cancellationToken);
        }
        catch (Exception ex)
        {
            await FailAsync(context, $"Unexpected ingest-submission error: {ex.Message}", cancellationToken);
        }
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

    private async Task<string> PersistOriginalAsync(string taskId, string extension, byte[] bytes, CancellationToken cancellationToken)
    {
        using var storeOriginalSpan = HubTracing.ActivitySource.StartActivity("hub.ingest_submission.store_original");
        storeOriginalSpan?.SetTag("task_id", taskId);

        var originalPath = await _sourceArtifactStore.PersistOriginalAsync(taskId, extension, bytes, cancellationToken);
        storeOriginalSpan?.SetTag("original_path", originalPath);
        storeOriginalSpan?.SetTag("size_bytes", bytes.LongLength);

        HubMetrics.RecordIngestSubmissionArtifactPersisted("original");
        IngestSubmissionLogEvents.LogOriginalPersisted(_logger, taskId, originalPath, bytes.LongLength,
            Path.GetExtension(originalPath));
        return originalPath;
    }

    private async Task TriggerAsync(PipelineContext context, SourceArtifactSet artifactSet, DateTimeOffset queuedAt, CancellationToken cancellationToken)
    {
        var taskId = context.TaskId;
        await _runGate.RunExclusiveAsync(async () =>
        {
            using var triggerSpan = HubTracing.ActivitySource.StartActivity("hub.ingest_run.trigger");
            triggerSpan?.SetTag("task_id", taskId);
            triggerSpan?.SetTag("dispatcher", "child_process");

            var queuedDurationMs = (long)(DateTimeOffset.UtcNow - queuedAt).TotalMilliseconds;
            HubMetrics.RecordIngestSubmissionQueueWait(taskId, queuedDurationMs / 1000.0);

            await _repository.UpsertAsync(new OperationalTaskState(taskId, "running", null, DateTimeOffset.UtcNow), cancellationToken);
            await _publisher.PublishAsync(taskId, "queued", "running", cancellationToken: cancellationToken);
            IngestSubmissionLogEvents.LogRunTriggered(_logger, taskId, queuedDurationMs);

            var request = new IngestAgentRequest(
                TaskId: taskId,
                SourceRef: artifactSet.NormalizedMarkdownPath,
                SourceKind: "file",
                PagesDir: _contentPaths.PagesDir,
                TasksDir: _contentPaths.TasksDir,
                IndexPath: _contentPaths.IndexPath,
                LogPath: _contentPaths.LogPath,
                PastedText: null,
                InstructionsDir: _contentPaths.InstructionsDir,
                PolicyPath: _contentPaths.PolicyPath);

            await _dispatcher.DispatchAsync(request, cancellationToken);
            await _repository.DeleteAsync(taskId, cancellationToken);

            if (File.Exists(context.TaskArtifactPath))
            {
                var finalMarkdown = await File.ReadAllTextAsync(context.TaskArtifactPath, cancellationToken);
                var final = TaskArtifactFrontmatter.TryParse(finalMarkdown);
                if (final is not null)
                {
                    await _publisher.PublishAsync(taskId, "running", final.Status, final.FailureReason, cancellationToken: cancellationToken);
                }
            }

            return true;
        }, cancellationToken);
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
            Narrative: narrative);

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

    private sealed record PipelineContext(string TaskId, string TaskArtifactPath, DateTimeOffset SubmittedAt);
}
