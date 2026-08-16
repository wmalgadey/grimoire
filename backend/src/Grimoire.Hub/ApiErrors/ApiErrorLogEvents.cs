using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.ApiErrors;

/// <summary>
/// Structured log events for the HTTP failure contract
/// (024 <c>plan.md ## Observability &gt; Structured Log Events</c>).
///
/// <para>
/// Two events rather than one, because the levels differ and so does what an operator does about
/// them: a declined request is the system working as designed and belongs at WARN; a fault is ours
/// and belongs at ERROR. Both carry <c>trace_id</c>, which is also the <c>traceId</c> written into
/// the response body — that shared value is what lets an operator join a screenshot to a log line.
/// </para>
///
/// <para>
/// EventId numbers use a fresh 100-block. The existing per-feature blocks collide across classes
/// (40–43, 60–65, 90 are each used twice), so only <c>EventId.Name</c> is a reliable identity —
/// which is what the contract tests assert.
/// </para>
/// </summary>
internal static class ApiErrorLogEvents
{
    private static readonly EventId DeclinedEvent = new(100, "api.error.declined");
    private static readonly EventId FaultedEvent = new(101, "api.error.faulted");

    public static void LogDeclined(ILogger logger, string code, int status, string path, string? traceId)
    {
        using var span = StartLogEventSpan("api.error.declined", "Warning", code, status);

        logger.LogWarning(DeclinedEvent,
            "API request declined. code={code} status={status} path={path} trace_id={trace_id}",
            code, status, SanitizeForLog(path), traceId ?? string.Empty);
    }

    /// <summary>
    /// <paramref name="failureReason"/> carries the exception's message on the unhandled path. It
    /// is a log field and only a log field — the response body carries the generic
    /// <c>internal_error</c> detail instead (024 FR-015, SC-008). An exception message can contain
    /// paths, connection strings, or provider text; the log is an operator surface, the HTTP
    /// response is not.
    /// </summary>
    public static void LogFaulted(
        ILogger logger, string code, int status, string path, string? traceId, string failureReason)
    {
        using var span = StartLogEventSpan("api.error.faulted", "Error", code, status);

        logger.LogError(FaultedEvent,
            "API request faulted. code={code} status={status} path={path} trace_id={trace_id} failure_reason={failure_reason}",
            code, status, SanitizeForLog(path), traceId ?? string.Empty, SanitizeForLog(failureReason));
    }

    /// <summary>
    /// The Hub's log-shaped-span idiom: a span named after the log event, carrying
    /// <c>signal_type=log</c>, so the event is queryable as both a log and a span and lands inside
    /// the request's trace. Its parent is the ambient ASP.NET Core request activity — the linkage
    /// feature 003 lost silently, and the reason the trace contract test asserts parentage rather
    /// than mere emission.
    /// </summary>
    private static Activity? StartLogEventSpan(string eventName, string level, string code, int status)
    {
        var span = HubTracing.ActivitySource.StartActivity(eventName);
        span?.SetTag("signal_type", "log");
        span?.SetTag("event_name", eventName);
        span?.SetTag("level", level);
        span?.SetTag("code", code);
        span?.SetTag("status", status);
        return span;
    }

    private static string SanitizeForLog(string? value) =>
        (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
}
