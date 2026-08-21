using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;

/// <summary>
/// #61 — structured log events for the per-agent <c>GRIMOIRE_*</c> environment overrides
/// the Hub hands to a child agent process (ADR-004). The precedence rule is the same for
/// every agent — the secrets file wins, the Hub's own environment is the fallback — and
/// these events make the outcome legible: which value an agent actually got, where it came
/// from, and, when both sources had one, that the inherited one was superseded rather than
/// silently dropped.
///
/// <para>
/// Only the <c>GRIMOIRE_*</c> variables travel through here. The credential
/// (<c>ANTHROPIC_AUTH_TOKEN</c>) is scoped separately by ADR-004 and is never logged.
/// </para>
/// </summary>
public static class AgentEnvironmentLogEvents
{
    /// <summary>The secrets file — the <c>.env</c> <see cref="LocalSecretsLoader"/> parses.</summary>
    public const string SecretsFileSource = "secrets_file";

    /// <summary>The Hub's own process environment, inherited by the child when the secrets file is silent.</summary>
    public const string ProcessEnvSource = "process_env";

    private static readonly EventId OverrideAppliedEvent = new(110, "agent.env.override_applied");
    private static readonly EventId OverrideSupersededEvent = new(111, "agent.env.override_superseded");

    public static void LogOverrideApplied(ILogger logger, string agent, string variable, string source, string value)
    {
        using var span = StartLogEventSpan("agent.env.override_applied", "Information");
        span?.SetTag("agent", agent);
        span?.SetTag("variable", variable);
        span?.SetTag("source", source);
        span?.SetTag("value", value);

        logger.LogInformation(OverrideAppliedEvent,
            "Agent environment override applied. agent={agent} variable={variable} source={source} value={value}",
            agent, variable, source, SanitizeForLog(value));
    }

    /// <summary>
    /// Both sources carried a value and the secrets file won. The point of the event is
    /// that the operator who set the variable in the Hub's environment — a shell before
    /// <c>dotnet run</c>, a launch profile's <c>env</c> block — finds out it did not reach
    /// the agent, instead of diagnosing it from the agent's behaviour.
    /// </summary>
    public static void LogOverrideSuperseded(ILogger logger, string agent, string variable, string supersededSource, string winningSource)
    {
        using var span = StartLogEventSpan("agent.env.override_superseded", "Information");
        span?.SetTag("agent", agent);
        span?.SetTag("variable", variable);
        span?.SetTag("superseded_source", supersededSource);
        span?.SetTag("winning_source", winningSource);

        logger.LogInformation(OverrideSupersededEvent,
            "Agent environment override superseded. agent={agent} variable={variable} superseded_source={superseded_source} winning_source={winning_source}",
            agent, variable, supersededSource, winningSource);
    }

    private static Activity? StartLogEventSpan(string eventName, string level)
    {
        var span = HubTracing.ActivitySource.StartActivity(eventName);
        span?.SetTag("signal_type", "log");
        span?.SetTag("event_name", eventName);
        span?.SetTag("level", level);
        return span;
    }

    private static string SanitizeForLog(string? value) =>
        (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
}
