using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.ApiErrors;

/// <summary>
/// The only producer of HTTP error responses in the Hub (ADR-026 rule BR1; 024 FR-001/FR-004).
/// Every declining or failing endpoint routes through here, so every failure reaches a client in
/// one shape: <c>application/problem+json</c> carrying <c>status</c>, <c>title</c>, <c>detail</c>,
/// <c>code</c>, and — when a trace context exists — <c>traceId</c>.
///
/// <para>
/// <b>Placement</b> is load-bearing and constrained by ADR-020: this type is called from HTTP
/// endpoint handlers only, never from a coordinator or transition service. Those are shared with
/// the CLI, whose failure contract is exit codes plus a stdout/stderr split — composing an HTTP
/// response shape inside them would push it into the CLI path.
/// </para>
/// </summary>
public static class ApiErrorResults
{
    /// <summary>
    /// The error response for <paramref name="code"/>.
    /// </summary>
    /// <param name="code">A catalogue code. An unknown one still yields readable prose (FR-016).</param>
    /// <param name="detail">
    /// Optional override for the catalogue's default detail, when the call site can say something
    /// more specific (naming a task id, a limit, a field). Same content rules as a catalogue
    /// default: readable prose, no identifiers, no serialized structures.
    /// </param>
    /// <param name="extensions">
    /// Optional RFC 7807 extension members for a failure that carries structured data a client
    /// genuinely needs — <c>unresolvedTaskIds</c> on a blocked lint trigger is the one real case,
    /// and it is asserted by <c>LintTriggerPreconditionTests</c>. Extensions sit alongside the five
    /// core members; they never replace them, so the one-shape guarantee holds.
    /// </param>
    public static IResult Problem(
        string code,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        => new ApiErrorResult(ApiErrorCatalogue.Resolve(code), detail, extensions);

    /// <summary>
    /// The composed error response.
    ///
    /// <para>
    /// It is an <see cref="IResult"/> rather than a pre-built payload so that composition happens
    /// during request execution: that is where the request path lives (a mandatory field of both
    /// declared log events) and where the request's own <see cref="Activity"/> is current, so the
    /// <c>traceId</c> written into the body is the one the operator will find in the trace. Reading
    /// it here rather than through ASP.NET Core's <c>CustomizeProblemDetails</c> hook also keeps the
    /// result a contract our own source decides, so a test asserting it can fail from a change to
    /// Grimoire (Constitution Principle II, "Test what we own") instead of asserting that the
    /// framework calls its own callback.
    /// </para>
    /// </summary>
    internal sealed class ApiErrorResult(
        ApiErrorDefinition definition,
        string? detailOverride,
        IReadOnlyDictionary<string, object?>? extensions) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var traceId = Activity.Current?.TraceId.ToString();
            var path = httpContext.Request.Path.Value ?? string.Empty;

            var problem = new ProblemDetails
            {
                Status = definition.Status,
                Title = definition.Title,
                Detail = string.IsNullOrWhiteSpace(detailOverride) ? definition.Detail : detailOverride,
            };

            problem.Extensions["code"] = definition.Code;

            // Omitted entirely rather than serialized empty, so a client never renders a blank
            // correlation id in its technical detail (024 spec edge case).
            if (traceId is not null)
            {
                problem.Extensions["traceId"] = traceId;
            }

            if (extensions is not null)
            {
                foreach (var (key, value) in extensions)
                {
                    problem.Extensions[key] = value;
                }
            }

            Emit(httpContext, definition, path, traceId, failureReason: null);

            httpContext.Response.StatusCode = definition.Status;
            httpContext.Response.ContentType = "application/problem+json";
            await httpContext.Response.WriteAsJsonAsync(problem, httpContext.RequestAborted);
        }
    }

    /// <summary>
    /// The metric and log event every envelope emits, shared with
    /// <see cref="ApiErrorExceptionHandler"/> so the deliberate and unhandled paths are
    /// indistinguishable to an observer.
    /// </summary>
    internal static void Emit(
        HttpContext httpContext,
        ApiErrorDefinition definition,
        string path,
        string? traceId,
        string? failureReason)
    {
        HubMetrics.RecordApiError(definition.Code, definition.Status);

        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Grimoire.Hub.ApiErrors");

        if (definition.Status >= 500)
        {
            ApiErrorLogEvents.LogFaulted(
                logger, definition.Code, definition.Status, path, traceId,
                failureReason ?? definition.Title);
        }
        else
        {
            ApiErrorLogEvents.LogDeclined(logger, definition.Code, definition.Status, path, traceId);
        }
    }
}
