using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.LintFindings;

/// <summary>
/// Hub-side structured log event for Findings Report persistence (plan.md ## Observability
/// > Structured Log Events, 013-lint-agent).
/// </summary>
public static class LintFindingsLogEvents
{
    private static readonly EventId FindingsReportCreatedEvent = new(90, "lint.findings_report.created");

    public static void LogFindingsReportCreated(ILogger logger, string runId, string path)
    {
        using var span = HubTracing.ActivitySource.StartActivity("lint.findings_report.created");
        span?.SetTag("signal_type", "log");
        span?.SetTag("event_name", "lint.findings_report.created");
        span?.SetTag("level", "Information");
        span?.SetTag("run_id", runId);
        span?.SetTag("path", path);

        logger.LogInformation(FindingsReportCreatedEvent,
            "Findings Report created. run_id={run_id} path={path}",
            runId, path);
    }
}
