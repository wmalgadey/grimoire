using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.Runtime.Paths;

/// <summary>
/// Structured log events for runtime path composition (plan.md ## Observability >
/// Structured Log Events, ADR-009). Each event starts a matching Activity span tagged
/// signal_type=log/event_name/level so logs and traces correlate.
/// </summary>
public static class GrimoirePathLogEvents
{
    private static readonly EventId PathsResolvedEvent = new(40, "paths_resolved");
    private static readonly EventId PathsLocationCreatedEvent = new(41, "paths_location_created");
    private static readonly EventId PathsValidationFailedEvent = new(42, "paths_validation_failed");

    /// <summary>Once per successful startup, after validation/creation, before serving.</summary>
    public static void LogPathsResolved(ILogger logger, ResolvedGrimoirePaths paths)
    {
        using var span = StartLogEventSpan("paths_resolved", "Information");
        span?.SetTag("base_dir", paths.BaseDir);
        span?.SetTag("data_dir", paths.DataDir);
        span?.SetTag("content_root", paths.ContentRoot);
        span?.SetTag("raw_dir", paths.RawOriginalsDir);
        span?.SetTag("state_db", paths.StateDbPath);
        span?.SetTag("secrets_file", paths.SecretsFilePath);
        span?.SetTag("instructions_dir", paths.InstructionsDir);
        span?.SetTag("agent_worker", paths.AgentWorkerPath);

        var sources = string.Join(", ", paths.Locations.Select(l => $"{l.Name}={l.Source}"));
        span?.SetTag("sources", sources);

        logger.LogInformation(PathsResolvedEvent,
            "Runtime paths resolved. base_dir={base_dir} data_dir={data_dir} content_root={content_root} " +
            "raw_dir={raw_dir} state_db={state_db} secrets_file={secrets_file} instructions_dir={instructions_dir} " +
            "agent_worker={agent_worker} sources={sources}",
            paths.BaseDir, paths.DataDir, paths.ContentRoot, paths.RawOriginalsDir, paths.StateDbPath,
            paths.SecretsFilePath, paths.InstructionsDir, paths.AgentWorkerPath, sources);
    }

    /// <summary>Each writable-data location auto-created at startup.</summary>
    public static void LogLocationCreated(ILogger logger, string location, string resolvedPath)
    {
        using var span = StartLogEventSpan("paths_location_created", "Information");
        span?.SetTag("location", location);
        span?.SetTag("resolved_path", resolvedPath);

        logger.LogInformation(PathsLocationCreatedEvent,
            "Runtime path location created. location={location} resolved_path={resolved_path}",
            location, resolvedPath);
    }

    /// <summary>A required input location is missing / wrong kind at startup, immediately before non-zero exit.</summary>
    public static void LogValidationFailed(ILogger logger, string location, string configuredValue, string resolvedPath, string reason)
    {
        using var span = StartLogEventSpan("paths_validation_failed", "Error");
        span?.SetTag("location", location);
        span?.SetTag("configured_value", configuredValue);
        span?.SetTag("resolved_path", resolvedPath);
        span?.SetTag("reason", reason);

        logger.LogError(PathsValidationFailedEvent,
            "Runtime path validation failed. location={location} configured_value={configured_value} resolved_path={resolved_path} reason={reason}",
            location, configuredValue, resolvedPath, reason);
    }

    private static Activity? StartLogEventSpan(string eventName, string level)
    {
        var span = HubTracing.ActivitySource.StartActivity(eventName);
        span?.SetTag("signal_type", "log");
        span?.SetTag("event_name", eventName);
        span?.SetTag("level", level);
        return span;
    }
}
