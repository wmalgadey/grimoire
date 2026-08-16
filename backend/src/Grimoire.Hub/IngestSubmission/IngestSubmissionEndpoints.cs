using Grimoire.Hub.ApiErrors;
using System.Text.Json;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.ContentRoot;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.Hub.IngestSubmission;

internal sealed record UrlSubmissionRequest(
    string Kind,
    string? Url,
    string? UserPrompt = null,
    Dictionary<string, bool>? ConvertSteps = null);

/// <summary>
/// HTTP endpoints for ingest submission and board data
/// (contracts/ingest-submission-api.md + 004 contracts/ingest-submission-api-extension.md).
/// </summary>
public static class IngestSubmissionEndpoints
{
    public static RouteGroupBuilder MapIngestSubmissionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", PostIngestSubmissionAsync);
        group.MapGet("/", GetBoardAsync);
        group.MapGet("/defaults", GetDefaultsAsync);
        group.MapGet("/{taskId}", GetTaskDetailAsync);
        group.MapGet("/{taskId}/task-record", GetTaskRecordAsync);
        group.MapGet("/{taskId}/source/original", GetSourceOriginalAsync);
        group.MapPost("/{taskId}/retrigger", PostRetriggerAsync);
        group.MapPost("/{taskId}/restart", PostRestartAsync);
        return group;
    }

    public static RouteGroupBuilder MapIngestQueueEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/resume", PostResumeAsync);
        return group;
    }

    private static async Task<IResult> PostIngestSubmissionAsync(
        HttpRequest request,
        IngestSubmissionValidator validator,
        IngestSubmissionPipeline pipeline,
        CancellationToken cancellationToken)
    {
        var logger = request.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(IngestSubmissionEndpoints));

        if (request.HasFormContentType)
        {
            return await HandleFileSubmissionAsync(request, validator, pipeline, logger, cancellationToken);
        }

        return await HandleUrlSubmissionAsync(request, validator, pipeline, logger, cancellationToken);
    }

    private static async Task<IResult> HandleUrlSubmissionAsync(
        HttpRequest request, IngestSubmissionValidator validator, IngestSubmissionPipeline pipeline, ILogger logger, CancellationToken cancellationToken)
    {
        UrlSubmissionRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<UrlSubmissionRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return ApiErrorResults.Problem(ApiErrorCatalogue.IngestSubmissionBodyInvalid);
        }

        if (body is null || !string.Equals(body.Kind, "url", StringComparison.OrdinalIgnoreCase))
        {
            HubMetrics.RecordIngestSubmission("url", "rejected");
            return ApiErrorResults.Problem(ApiErrorCatalogue.IngestSubmissionKindInvalid,
                "A JSON submission must carry a URL. Submit a URL, or send a file instead.");
        }

        var validation = validator.ValidateUrl(body.Url);
        if (!validation.IsValid)
        {
            HubMetrics.RecordIngestSubmission("url", "rejected");
            return ToErrorResult(validation);
        }

        var configValidation = ValidateSubmissionConfig(
            validator, logger, "url", body.UserPrompt, body.ConvertSteps, out var normalizedPrompt);
        if (configValidation is not null)
        {
            return configValidation;
        }

        var taskId = await pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.Url, body.Url, null, null, null,
                UserPrompt: normalizedPrompt, ConvertSteps: body.ConvertSteps), cancellationToken);

        return Results.Accepted(value: new
        {
            taskId,
            status = "received",
            sourceKind = "url",
            acceptedAt = DateTimeOffset.UtcNow,
            userPromptSource = normalizedPrompt is null ? "default" : "custom",
            convertSteps = ConvertStepRegistry.ResolveEffective("url", body.ConvertSteps),
        });
    }

    private static async Task<IResult> HandleFileSubmissionAsync(
        HttpRequest request, IngestSubmissionValidator validator, IngestSubmissionPipeline pipeline, ILogger logger, CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var rawKind = form["kind"].ToString();

        if (!IngestSubmissionValidator.TryParseKind(rawKind, out var kind) || kind == IngestSubmissionKind.Url)
        {
            HubMetrics.RecordIngestSubmission(rawKind, "rejected");
            return ApiErrorResults.Problem(ApiErrorCatalogue.IngestSubmissionKindInvalid,
                "A file submission must be a Markdown, PDF, or Office document.");
        }

        var file = form.Files["file"];
        if (file is null || file.Length == 0 && form.Files.Count == 0)
        {
            HubMetrics.RecordIngestSubmission(rawKind, "rejected");
            return ApiErrorResults.Problem(ApiErrorCatalogue.IngestSubmissionFileMissing);
        }

        var validation = validator.ValidateFile(kind, file.FileName, file.Length);
        if (!validation.IsValid)
        {
            HubMetrics.RecordIngestSubmission(rawKind, "rejected");
            return ToErrorResult(validation);
        }

        Dictionary<string, bool>? convertSteps = null;
        var rawSteps = form["convertSteps"].ToString();
        if (!string.IsNullOrWhiteSpace(rawSteps))
        {
            try
            {
                convertSteps = JsonSerializer.Deserialize<Dictionary<string, bool>>(rawSteps);
            }
            catch (JsonException)
            {
                HubMetrics.RecordIngestSubmission(rawKind, "rejected");
                return ApiErrorResults.Problem(ApiErrorCatalogue.IngestSubmissionConvertStepsInvalid);
            }
        }

        var configValidation = ValidateSubmissionConfig(
            validator, logger, rawKind, form["userPrompt"].ToString(), convertSteps, out var normalizedPrompt);
        if (configValidation is not null)
        {
            return configValidation;
        }

        await using var stream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        var taskId = await pipeline.AcceptAsync(
            new IngestSubmissionInput(kind, null, file.FileName, memoryStream.ToArray(), file.ContentType,
                UserPrompt: normalizedPrompt, ConvertSteps: convertSteps), cancellationToken);

        return Results.Accepted(value: new
        {
            taskId,
            status = "received",
            sourceKind = rawKind,
            acceptedAt = DateTimeOffset.UtcNow,
            userPromptSource = normalizedPrompt is null ? "default" : "custom",
            convertSteps = ConvertStepRegistry.ResolveEffective(rawKind, convertSteps),
        });
    }

    /// <summary>
    /// Shared 004 config validation for both submission shapes: user prompt (FR-010) and
    /// convert steps (FR-011/FR-013), all rejected before a task is created. Returns the
    /// error result, or null when the configuration is valid.
    /// </summary>
    private static IResult? ValidateSubmissionConfig(
        IngestSubmissionValidator validator,
        ILogger logger,
        string kindLabel,
        string? userPrompt,
        IReadOnlyDictionary<string, bool>? convertSteps,
        out string? normalizedPrompt)
    {
        var promptValidation = validator.ValidateUserPrompt(userPrompt, out normalizedPrompt);
        if (!promptValidation.IsValid)
        {
            HubMetrics.RecordIngestSubmission(kindLabel, "rejected");
            IngestSubmissionLogEvents.LogConfigRejected(logger, kindLabel, promptValidation.ErrorMessage!);
            return ToErrorResult(promptValidation);
        }

        var stepsValidation = validator.ValidateConvertSteps(kindLabel, convertSteps);
        if (!stepsValidation.IsValid)
        {
            HubMetrics.RecordIngestSubmission(kindLabel, "rejected");
            IngestSubmissionLogEvents.LogConfigRejected(logger, kindLabel, stepsValidation.ErrorMessage!);
            return ToErrorResult(stepsValidation);
        }

        return null;
    }

    /// <summary>
    /// Single source of truth for the submission form (004 FR-006/FR-011): the verbatim
    /// default user prompt and the convert-step registry. Fail-closed: a missing/empty
    /// default-prompt document is a 500 with a human-readable reason.
    /// </summary>
    private static async Task<IResult> GetDefaultsAsync(
        [FromServices] ResolvedGrimoirePaths resolvedPaths, CancellationToken cancellationToken)
    {
        // Non-null for Ingest (AgentRuntimePaths doc comment); nullable only for Query/Lint.
        var defaultUserPromptPath = resolvedPaths.Ingest.DefaultUserPromptPath!;

        if (!File.Exists(defaultUserPromptPath))
        {
return ApiErrorResults.Problem(ApiErrorCatalogue.DefaultUserPromptMissing);
        }

        var defaultUserPrompt = await File.ReadAllTextAsync(defaultUserPromptPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(defaultUserPrompt))
        {
return ApiErrorResults.Problem(ApiErrorCatalogue.DefaultUserPromptEmpty);
        }

        return Results.Ok(new
        {
            defaultUserPrompt = defaultUserPrompt.Trim(),
            userPromptMaxLength = IngestSubmissionValidator.UserPromptMaxLength,
            convertSteps = ConvertStepRegistry.All.Select(step => new
            {
                name = step.Name,
                appliesTo = step.AppliesTo.Order().ToArray(),
                requiredFor = step.RequiredFor.Order().ToArray(),
                defaultEnabled = step.DefaultEnabled,
            }),
        });
    }

    private static async Task<IResult> GetBoardAsync(
        KanbanBoardProjectionStore store, IngestContentPaths contentPaths, IngestRunCoordinator coordinator, CancellationToken cancellationToken)
    {
        var tasks = await store.GetAllAsync(contentPaths.TasksDir, cancellationToken);
        var queuePositions = await coordinator.GetQueuePositionsAsync(cancellationToken);
        var queuePaused = await coordinator.IsQueuePausedAsync(cancellationToken);

        return Results.Ok(new
        {
            tasks = tasks.Select(t => new
            {
                taskId = t.TaskId,
                status = t.Column,
                title = t.Title,
                updatedAt = t.UpdatedAt,
                failureReason = t.FailureReason,
                taskLink = t.TaskLink,
                queuePosition = queuePositions.TryGetValue(t.TaskId, out var position) ? (int?)position : null,
            }),
            queuePaused,
        });
    }

    private static async Task<IResult> GetTaskDetailAsync(
        string taskId,
        KanbanBoardProjectionStore store,
        Conversion.SourceArtifactStore sourceArtifactStore,
        IngestContentPaths contentPaths,
        IngestRunCoordinator coordinator,
        // Explicit: without it Minimal APIs infer a complex type as the request body, which
        // fails endpoint construction for a GET (and would bind from the wire, not DI).
        [FromServices] OperationalState.OperationalStateRepository stateRepository,
        CancellationToken cancellationToken)
    {
        var projection = await store.GetByTaskIdAsync(contentPaths.TasksDir, taskId, cancellationToken);
        if (projection is null)
        {
            return ApiErrorResults.Problem(ApiErrorCatalogue.IngestTaskNotFound);
        }

        var artifactSet = await sourceArtifactStore.TryReadMetadataAsync(taskId, cancellationToken);

        // 004: prompt/config recorded on the artifact (FR-009/FR-014); pre-004 tasks
        // return nulls — "defaults of their time".
        var artifactPath = Path.Combine(contentPaths.TasksDir, $"{taskId}.md");
        TaskArtifactFrontmatter? frontmatter = null;
        string? userPrompt = null;
        if (File.Exists(artifactPath))
        {
            var markdown = await File.ReadAllTextAsync(artifactPath, cancellationToken);
            frontmatter = TaskArtifactFrontmatter.TryParse(markdown);
            userPrompt = TaskArtifactFrontmatter.TryExtractUserPrompt(markdown);
        }

        var activity = coordinator.GetActivity(taskId);

        // 023 T008 (FR-006/SC-004): the ordered status "path". Empty for a task with no
        // recorded transitions.
        var statusHistory = await stateRepository.GetStatusHistoryAsync(taskId, cancellationToken);

        // 023 T024 (FR-001/FR-002, SC-001/SC-002): derived server-side so the URL-vs-file
        // split and the availability check live in exactly one tested place — the client
        // never has to guess from sourceRef.
        var source = ResolveSourceLink(taskId, artifactSet);

        return Results.Ok(new
        {
            taskId = projection.TaskId,
            status = projection.Column,
            // 023 T021 (FR-003/FR-004): the human-readable label, resolved by the same chain
            // the board uses so the two can never disagree. The raw id stays right beside it.
            title = projection.Title,
            failureReason = projection.FailureReason,
            statusHistory = statusHistory.Select(entry => new
            {
                status = entry.Status,
                enteredAt = entry.EnteredAt,
                detail = entry.Detail,
            }),
            source,
            sourceRef = artifactSet?.NormalizedMarkdownPath,
            originalRef = artifactSet?.OriginalPath,
            userPromptSource = frontmatter?.UserPromptSource,
            userPrompt,
            convertSteps = frontmatter?.ConvertSteps,
            runActivity = activity is null ? null : new
            {
                modelTurns = activity.ModelTurns,
                toolCalls = activity.ToolCalls,
                toolCallsByName = activity.ToolCallsByName,
                currentAction = activity.CurrentAction,
                lastEventAt = activity.LastEventAt,
            },
        });
    }

    /// <summary>
    /// The URL-vs-file split and availability check (023 T024, data-model.md §4). A URL
    /// submission links directly to what was submitted; a file submission links to the
    /// serve endpoint below — but only when the manifest AND the persisted original both
    /// still exist, so <c>available:false</c> is exactly the condition FR-002 needs the
    /// client to render as "unavailable" instead of a link that 404s when clicked.
    /// </summary>
    private static object ResolveSourceLink(string taskId, Conversion.SourceArtifactSet? artifactSet)
    {
        if (artifactSet?.SourceUrl is { Length: > 0 } url)
        {
            return new { kind = "url", href = url, available = true };
        }

        var available = artifactSet is not null && File.Exists(artifactSet.OriginalPath);
        return new
        {
            kind = "file",
            href = available ? $"/api/ingest-submissions/{taskId}/source/original" : null,
            available,
        };
    }

    /// <summary>
    /// Read-only stream of the persisted original (023 T024, FR-001/FR-002, SC-001/SC-002).
    /// The path is composed exclusively from the validated route <c>taskId</c> — via the
    /// manifest the Hub itself wrote, itself keyed only by <c>taskId</c> — so this endpoint
    /// accepts no other path input and has no traversal surface to guard.
    /// </summary>
    private static async Task<IResult> GetSourceOriginalAsync(
        string taskId,
        Conversion.SourceArtifactStore sourceArtifactStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(IngestSubmissionEndpoints));

        using var span = HubTracing.ActivitySource.StartActivity("hub.ingest_source.serve");
        span?.SetTag("task_id", taskId);

        var manifest = await sourceArtifactStore.TryReadMetadataAsync(taskId, cancellationToken);
        if (manifest is null || !File.Exists(manifest.OriginalPath))
        {
            span?.SetTag("result", "not_found");
            HubMetrics.RecordSourceContentRead("not_found");
            return ApiErrorResults.Problem(ApiErrorCatalogue.IngestSourceContentNotFound);
        }

        span?.SetTag("result", "served");
        HubMetrics.RecordSourceContentRead("served");
        IngestSubmissionLogEvents.LogSourceServed(logger, taskId, manifest.OriginalContentType);

        // Results.File's FileDownloadName always writes `Content-Disposition: attachment`;
        // FR-001 wants the original to open in the browser, so the header is set explicitly.
        return new InlineFileResult(manifest.OriginalPath, manifest.OriginalContentType);
    }

    /// <summary>
    /// <c>Content-Disposition: inline</c> file result (023 T024) — the one shape
    /// <see cref="Results.File(string, string?, bool)"/> cannot produce, since its
    /// download-name parameter always writes <c>attachment</c>.
    /// </summary>
    private sealed class InlineFileResult(string path, string contentType) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = contentType;
            httpContext.Response.Headers.ContentDisposition = "inline";
            await using var stream = File.OpenRead(path);
            httpContext.Response.ContentLength = stream.Length;
            await stream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
        }
    }

    /// <summary>
    /// Serves the rendered task record (006 FR-006/FR-007, contracts/task-record-api.md):
    /// parsed frontmatter as <c>metadata</c> plus the markdown body with the frontmatter
    /// block stripped. Missing file or unparseable frontmatter both map to 404 — never a
    /// 5xx for a malformed record. Leaves the existing detail/board endpoints untouched
    /// (FR-012).
    /// </summary>
    private static async Task<IResult> GetTaskRecordAsync(
        string taskId,
        TaskRecordReadModel readModel,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(IngestSubmissionEndpoints));

        using var span = HubTracing.ActivitySource.StartActivity("hub.task_record.serve");
        span?.SetTag("task_id", taskId);

        var result = await readModel.ReadAsync(taskId, cancellationToken);
        var outcome = result.Outcome switch
        {
            TaskRecordOutcome.Ok => "ok",
            TaskRecordOutcome.Missing => "missing",
            TaskRecordOutcome.Unparseable => "unparseable",
            _ => "unknown",
        };
        span?.SetTag("outcome", outcome);

        var contentLength = result.Record?.Body.Length ?? 0;
        IngestSubmissionLogEvents.LogTaskRecordServed(logger, taskId, outcome, contentLength);
        HubMetrics.RecordTaskRecordRead(outcome);

        if (result.Outcome != TaskRecordOutcome.Ok)
        {
            return ApiErrorResults.Problem(ApiErrorCatalogue.IngestTaskRecordUnavailable);
        }

        var record = result.Record!;
        return Results.Ok(new
        {
            taskId = record.TaskId,
            metadata = new
            {
                status = record.Metadata.Status,
                agent = record.Metadata.Agent,
                startedAt = record.Metadata.StartedAt,
                completedAt = record.Metadata.CompletedAt,
                sourceRef = record.Metadata.SourceRef,
                originalRef = record.Metadata.OriginalRef,
                failureReason = record.Metadata.FailureReason,
            },
            body = record.Body,
        });
    }

    /// <summary>Re-arms a single queued task after a Hub restart (004 FR-021).</summary>
    private static async Task<IResult> PostRetriggerAsync(
        string taskId, IngestRunCoordinator coordinator, KanbanBoardProjectionStore store, IngestContentPaths contentPaths, CancellationToken cancellationToken)
    {
        var projection = await store.GetByTaskIdAsync(contentPaths.TasksDir, taskId, cancellationToken);
        if (projection is null)
        {
            return ApiErrorResults.Problem(ApiErrorCatalogue.IngestTaskNotFound);
        }

        var retriggered = await coordinator.RetriggerAsync(taskId, cancellationToken);
        return retriggered
            ? Results.Ok(new { taskId, retriggered = true })
            : ApiErrorResults.Problem(ApiErrorCatalogue.IngestTaskNotQueued,
                $"This task has already moved on from the queue (it is now {projection.Column}), so it cannot be changed there.");
    }

    /// <summary>
    /// Manual restart of a finally-failed task (023 T030, FR-010..FR-013, SC-007/SC-008).
    /// Thin wrapper over the coordinator method, preserving the shared-coordinator parity
    /// pattern (ADR-020) — <c>ingest-retrigger</c> keeps its own, distinct meaning.
    /// </summary>
    private static async Task<IResult> PostRestartAsync(
        string taskId,
        IngestRunCoordinator coordinator,
        KanbanBoardProjectionStore store,
        Conversion.SourceArtifactStore sourceArtifactStore,
        IngestContentPaths contentPaths,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(IngestSubmissionEndpoints));

        using var span = HubTracing.ActivitySource.StartActivity("hub.ingest_task.restart");
        span?.SetTag("task_id", taskId);

        var projection = await store.GetByTaskIdAsync(contentPaths.TasksDir, taskId, cancellationToken);
        if (projection is null)
        {
            span?.SetTag("outcome", "not_found");
            return ApiErrorResults.Problem(ApiErrorCatalogue.IngestTaskNotFound);
        }

        if (projection.Column != "failed")
        {
            return Reject(span, logger, taskId, projection.Column,
                ApiErrorCatalogue.RestartTaskNotFailed);
        }

        var manifest = await sourceArtifactStore.TryReadMetadataAsync(taskId, cancellationToken);
        if (manifest is null || !File.Exists(manifest.NormalizedMarkdownPath))
        {
            return Reject(span, logger, taskId, projection.Column,
                ApiErrorCatalogue.RestartSourceMissing);
        }

        var artifactPath = Path.Combine(contentPaths.TasksDir, $"{taskId}.md");
        string? userPrompt = File.Exists(artifactPath)
            ? TaskArtifactFrontmatter.TryExtractUserPrompt(await File.ReadAllTextAsync(artifactPath, cancellationToken))
            : null;

        var accepted = await coordinator.RestartFailedAsync(taskId, manifest.NormalizedMarkdownPath, userPrompt, cancellationToken);
        if (!accepted)
        {
            return Reject(span, logger, taskId, projection.Column,
                ApiErrorCatalogue.RestartAlreadyInProgress);
        }

        span?.SetTag("outcome", "accepted");
        HubMetrics.RecordRestart("accepted");
        IngestSubmissionLogEvents.LogTaskRestarted(logger, taskId);

        return Results.Accepted(value: new { taskId, status = "queued" });
    }

    /// <summary>
    /// ADR-025 fixes two distinct restart declines by semantics (task not <c>failed</c>; normalized
    /// source missing) and the coordinator's compare-and-swap adds a third (a concurrent restart
    /// already won). Each carries its own catalogue code rather than one shared "restart rejected",
    /// because ADR-018's rule that the caller sees the actual outcome applies here too — and
    /// because only one of the three has a way forward the user can act on.
    /// </summary>
    private static IResult Reject(
        System.Diagnostics.Activity? span, ILogger logger, string taskId, string currentStatus, string code)
    {
        span?.SetTag("outcome", "rejected");
        HubMetrics.RecordRestart("rejected");
        IngestSubmissionLogEvents.LogTaskRestartRejected(logger, taskId, currentStatus);
        return ApiErrorResults.Problem(code);
    }

    /// <summary>Resumes automatic queue processing after a Hub restart (004 FR-021); idempotent.</summary>
    private static async Task<IResult> PostResumeAsync(IngestRunCoordinator coordinator, CancellationToken cancellationToken)
    {
        var queuedTasks = await coordinator.ResumeAsync(cancellationToken);
        return Results.Ok(new { queuePaused = false, queuedTasks });
    }

    private static IResult ToErrorResult(IngestSubmissionValidationResult validation) => validation.ErrorKind switch
    {
        IngestSubmissionValidationErrorKind.UnsupportedMediaType =>
            ApiErrorResults.Problem(ApiErrorCatalogue.IngestSubmissionUnsupportedMediaType, validation.ErrorMessage),
        IngestSubmissionValidationErrorKind.UnprocessableEntity =>
            ApiErrorResults.Problem(ApiErrorCatalogue.IngestSubmissionUnprocessable, validation.ErrorMessage),
        _ => ApiErrorResults.Problem(ApiErrorCatalogue.IngestSubmissionInvalid, validation.ErrorMessage),
    };
}
