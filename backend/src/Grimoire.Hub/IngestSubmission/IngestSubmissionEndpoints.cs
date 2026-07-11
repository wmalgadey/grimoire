using Grimoire.Domain.Ingest;

namespace Grimoire.Hub.IngestSubmission;

internal sealed record UrlSubmissionRequest(string Kind, string? Url);

/// <summary>
/// HTTP endpoints for ingest submission and board data (contracts/ingest-submission-api.md).
/// </summary>
public static class IngestSubmissionEndpoints
{
    public static RouteGroupBuilder MapIngestSubmissionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", PostIngestSubmissionAsync);
        group.MapGet("/", GetBoardAsync);
        group.MapGet("/{taskId}", GetTaskDetailAsync);
        return group;
    }

    private static async Task<IResult> PostIngestSubmissionAsync(
        HttpRequest request,
        IngestSubmissionValidator validator,
        IngestSubmissionPipeline pipeline,
        CancellationToken cancellationToken)
    {
        if (request.HasFormContentType)
        {
            return await HandleFileSubmissionAsync(request, validator, pipeline, cancellationToken);
        }

        return await HandleUrlSubmissionAsync(request, validator, pipeline, cancellationToken);
    }

    private static async Task<IResult> HandleUrlSubmissionAsync(
        HttpRequest request, IngestSubmissionValidator validator, IngestSubmissionPipeline pipeline, CancellationToken cancellationToken)
    {
        UrlSubmissionRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<UrlSubmissionRequest>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
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

        var taskId = await pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.Url, body.Url, null, null, null), cancellationToken);

        return Results.Accepted(value: new
        {
            taskId,
            status = "received",
            sourceKind = "url",
            acceptedAt = DateTimeOffset.UtcNow,
        });
    }

    private static async Task<IResult> HandleFileSubmissionAsync(
        HttpRequest request, IngestSubmissionValidator validator, IngestSubmissionPipeline pipeline, CancellationToken cancellationToken)
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

        await using var stream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        var taskId = await pipeline.AcceptAsync(
            new IngestSubmissionInput(kind, null, file.FileName, memoryStream.ToArray(), file.ContentType), cancellationToken);

        return Results.Accepted(value: new
        {
            taskId,
            status = "received",
            sourceKind = rawKind,
            acceptedAt = DateTimeOffset.UtcNow,
        });
    }

    private static async Task<IResult> GetBoardAsync(KanbanBoardProjectionStore store, ContentRoot.ContentRootPaths contentPaths, CancellationToken cancellationToken)
    {
        var tasks = await store.GetAllAsync(contentPaths.TasksDir, cancellationToken);
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
            }),
        });
    }

    private static async Task<IResult> GetTaskDetailAsync(
        string taskId, KanbanBoardProjectionStore store, Conversion.SourceArtifactStore sourceArtifactStore, ContentRoot.ContentRootPaths contentPaths, CancellationToken cancellationToken)
    {
        var projection = await store.GetByTaskIdAsync(contentPaths.TasksDir, taskId, cancellationToken);
        if (projection is null)
        {
            return Results.NotFound(new { message = $"Task '{taskId}' was not found." });
        }

        var artifactSet = await sourceArtifactStore.TryReadMetadataAsync(taskId, cancellationToken);

        return Results.Ok(new
        {
            taskId = projection.TaskId,
            status = projection.Column,
            failureReason = projection.FailureReason,
            sourceRef = artifactSet?.NormalizedMarkdownPath,
            originalRef = artifactSet?.OriginalPath,
        });
    }

    private static IResult ToErrorResult(IngestSubmissionValidationResult validation) => validation.ErrorKind switch
    {
        IngestSubmissionValidationErrorKind.UnsupportedMediaType => Results.Json(new { message = validation.ErrorMessage }, statusCode: StatusCodes.Status415UnsupportedMediaType),
        IngestSubmissionValidationErrorKind.UnprocessableEntity => Results.UnprocessableEntity(new { message = validation.ErrorMessage }),
        _ => Results.BadRequest(new { message = validation.ErrorMessage }),
    };
}
