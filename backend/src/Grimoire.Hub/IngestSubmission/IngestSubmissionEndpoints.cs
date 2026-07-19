using System.Text.Json;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.ContentRoot;
using Microsoft.Extensions.Logging;

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
        group.MapPost("/{taskId}/retrigger", PostRetriggerAsync);
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
            return Results.BadRequest(new { message = "Request body is not valid JSON." });
        }

        if (body is null || !string.Equals(body.Kind, "url", StringComparison.OrdinalIgnoreCase))
        {
            HubMetrics.RecordIngestSubmission("url", "rejected");
            return Results.BadRequest(new { message = "kind must be 'url' for a JSON submission." });
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
            return Results.BadRequest(new { message = "kind must be one of markdown_file, pdf_file, office_file for a file submission." });
        }

        var file = form.Files["file"];
        if (file is null || file.Length == 0 && form.Files.Count == 0)
        {
            HubMetrics.RecordIngestSubmission(rawKind, "rejected");
            return Results.BadRequest(new { message = "A file is required." });
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
                return Results.BadRequest(new { message = "convertSteps must be a JSON object of step name to boolean." });
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
    private static async Task<IResult> GetDefaultsAsync(ContentRootPaths contentPaths, CancellationToken cancellationToken)
    {
        if (!File.Exists(contentPaths.DefaultUserPromptPath))
        {
            return Results.Json(new
            {
                message = $"Default user prompt document not found at '{contentPaths.DefaultUserPromptPath}'.",
            }, statusCode: StatusCodes.Status500InternalServerError);
        }

        var defaultUserPrompt = await File.ReadAllTextAsync(contentPaths.DefaultUserPromptPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(defaultUserPrompt))
        {
            return Results.Json(new
            {
                message = $"Default user prompt document at '{contentPaths.DefaultUserPromptPath}' is empty.",
            }, statusCode: StatusCodes.Status500InternalServerError);
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
        KanbanBoardProjectionStore store, ContentRootPaths contentPaths, IngestRunCoordinator coordinator, CancellationToken cancellationToken)
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
        ContentRootPaths contentPaths,
        IngestRunCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var projection = await store.GetByTaskIdAsync(contentPaths.TasksDir, taskId, cancellationToken);
        if (projection is null)
        {
            return Results.NotFound(new { message = $"Task '{taskId}' was not found." });
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

        return Results.Ok(new
        {
            taskId = projection.TaskId,
            status = projection.Column,
            failureReason = projection.FailureReason,
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
            return Results.NotFound(new { message = $"Task record for '{taskId}' is not available." });
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
        string taskId, IngestRunCoordinator coordinator, KanbanBoardProjectionStore store, ContentRootPaths contentPaths, CancellationToken cancellationToken)
    {
        var projection = await store.GetByTaskIdAsync(contentPaths.TasksDir, taskId, cancellationToken);
        if (projection is null)
        {
            return Results.NotFound(new { message = $"Task '{taskId}' was not found." });
        }

        var retriggered = await coordinator.RetriggerAsync(taskId, cancellationToken);
        return retriggered
            ? Results.Ok(new { taskId, retriggered = true })
            : Results.Conflict(new { message = $"Task '{taskId}' is not in the queue ({projection.Column})." });
    }

    /// <summary>Resumes automatic queue processing after a Hub restart (004 FR-021); idempotent.</summary>
    private static async Task<IResult> PostResumeAsync(IngestRunCoordinator coordinator, CancellationToken cancellationToken)
    {
        var queuedTasks = await coordinator.ResumeAsync(cancellationToken);
        return Results.Ok(new { queuePaused = false, queuedTasks });
    }

    private static IResult ToErrorResult(IngestSubmissionValidationResult validation) => validation.ErrorKind switch
    {
        IngestSubmissionValidationErrorKind.UnsupportedMediaType => Results.Json(new { message = validation.ErrorMessage }, statusCode: StatusCodes.Status415UnsupportedMediaType),
        IngestSubmissionValidationErrorKind.UnprocessableEntity => Results.UnprocessableEntity(new { message = validation.ErrorMessage }),
        _ => Results.BadRequest(new { message = validation.ErrorMessage }),
    };
}
