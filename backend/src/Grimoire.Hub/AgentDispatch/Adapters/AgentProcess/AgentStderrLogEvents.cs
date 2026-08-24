using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;

/// <summary>
/// Issue #183: every spawn path redirects a child agent's stderr
/// (<c>RedirectStandardError = true</c>) but, outside the manual <c>submit-source</c> CLI
/// path, nothing ever read it — the one place an agent's own structured logging and
/// unhandled-exception stack traces land (stdout is the ADR-008 NDJSON event channel;
/// "all logging goes to stderr/OTLP" per <c>Grimoire.IngestAgent/Program.cs</c>) was an
/// unread pipe. Two consequences: diagnostics were unreachable from <c>docker logs</c>,
/// and a talkative agent could fill the pipe's 64 KiB kernel buffer and block forever on
/// its next stderr write, wedging the run.
///
/// <see cref="AgentProcessHost.ProcessHandle"/> now drains stderr on every dispatch path
/// and re-logs each line here — never onto the ADR-008 channel (stderr carries no run
/// state; nothing parses it as one) — so it reaches <c>docker logs</c> and the Hub's own
/// OTLP export exactly like every other operator-facing signal (Constitution IV).
/// </summary>
public static class AgentStderrLogEvents
{
    private static readonly EventId AgentStderrEvent = new(120, "agent_stderr");

    public static void LogStderrLine(ILogger logger, string agent, string taskId, string line)
    {
        using var span = StartLogEventSpan();
        span?.SetTag("agent", agent);
        span?.SetTag("task_id", taskId);

        logger.LogWarning(AgentStderrEvent,
            "agent_stderr agent={agent} task_id={task_id} line={line}",
            agent, taskId, SanitizeForLog(line));
    }

    private static Activity? StartLogEventSpan()
    {
        var span = HubTracing.ActivitySource.StartActivity("agent_stderr");
        span?.SetTag("signal_type", "log");
        span?.SetTag("event_name", "agent_stderr");
        span?.SetTag("level", "Warning");
        return span;
    }

    private static string SanitizeForLog(string value) =>
        value.Replace("\r", string.Empty).Replace("\n", string.Empty);
}
