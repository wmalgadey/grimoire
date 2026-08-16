using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Hub.ApiErrors;

/// <summary>
/// Brings the unhandled-exception path into the same envelope as every deliberate rejection
/// (024 FR-004, FR-013).
///
/// <para>
/// Before this, the Hub had no exception middleware at all: an exception escaping a minimal-API
/// handler fell through to Kestrel as a bare 500 with an empty body — a third shape, alongside the
/// two ad-hoc ones, and the only one a client could not read anything from. A client that must
/// branch on "is this our envelope or a bare 500" is a client that ends up printing raw responses,
/// which is the defect this feature exists to remove.
/// </para>
///
/// <para>
/// <b>The exception's own text never reaches the response.</b> It goes to
/// <c>api.error.faulted</c>'s <c>failure_reason</c> field and stays there (FR-015, SC-008). An
/// exception message can carry filesystem paths, connection details, or upstream provider text;
/// the log is an operator surface, an HTTP response is not. The two are joined by <c>trace_id</c>,
/// which is what makes the generic response body diagnosable without making it revealing.
/// </para>
/// </summary>
internal sealed class ApiErrorExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var definition = ApiErrorCatalogue.Resolve(ApiErrorCatalogue.InternalError);
        var traceId = Activity.Current?.TraceId.ToString();
        var path = httpContext.Request.Path.Value ?? string.Empty;

        ApiErrorResults.Emit(httpContext, definition, path, traceId, failureReason: exception.Message);

        var problem = new ProblemDetails
        {
            Status = definition.Status,
            Title = definition.Title,
            Detail = definition.Detail,
        };

        problem.Extensions["code"] = definition.Code;
        if (traceId is not null)
        {
            problem.Extensions["traceId"] = traceId;
        }

        httpContext.Response.StatusCode = definition.Status;
        await httpContext.Response.WriteAsJsonAsync(
            problem, options: null, contentType: ApiErrorResults.ProblemJsonContentType, cancellationToken);

        return true;
    }
}
