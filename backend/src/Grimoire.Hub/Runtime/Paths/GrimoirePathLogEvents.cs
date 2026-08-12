using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.Runtime.Paths;

/// <summary>
/// Structured log events for runtime path composition (plan.md ## Observability >
/// Structured Log Events, ADR-022). Each event starts a matching Activity span tagged
/// signal_type=log/event_name/level so logs and traces correlate.
/// </summary>
public static class GrimoirePathLogEvents
{
    private static readonly EventId PathsResolvedEvent = new(40, "paths_resolved");
    private static readonly EventId PathsLocationCreatedEvent = new(41, "paths_location_created");
    private static readonly EventId PathsValidationFailedEvent = new(42, "paths_validation_failed");
    private static readonly EventId PathsConfigurationMissingEvent = new(43, "paths_configuration_missing");
    private static readonly EventId PathsConfigurationSupersededEvent = new(44, "paths_configuration_superseded");

    /// <summary>Once per successful startup, after validation/creation, before serving.</summary>
    public static void LogPathsResolved(ILogger logger, ResolvedGrimoirePaths paths)
    {
        using var span = StartLogEventSpan("paths_resolved", "Information");
        span?.SetTag("data_dir", paths.DataDir);
        span?.SetTag("wiki_dir", paths.WikiDir);
        span?.SetTag("agent_dir", paths.AgentDir);
        span?.SetTag("memory_dir", paths.MemoryDir);
        span?.SetTag("secrets_file", paths.SecretsFilePath);
        span?.SetTag("state_db", paths.StateDbPath);
        span?.SetTag("raw_dir", paths.RawOriginalsDir);

        var sources = string.Join(", ", paths.Locations.Select(l => $"{l.Name}={l.Source}"));
        span?.SetTag("sources", sources);

        logger.LogInformation(PathsResolvedEvent,
            "Runtime paths resolved. data_dir={data_dir} wiki_dir={wiki_dir} agent_dir={agent_dir} " +
            "memory_dir={memory_dir} secrets_file={secrets_file} state_db={state_db} raw_dir={raw_dir} sources={sources}",
            paths.DataDir, paths.WikiDir, paths.AgentDir, paths.MemoryDir, paths.SecretsFilePath, paths.StateDbPath,
            paths.RawOriginalsDir, sources);
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

    /// <summary>
    /// A required root is absent from every configuration tier, immediately before
    /// non-zero exit (ADR-022 FR-005/SC-006) — the versioned configuration file is the
    /// single source of default paths, so this is loud and specific rather than a silent
    /// fallback.
    /// </summary>
    public static void LogConfigurationMissing(ILogger logger, string configurationFile, IReadOnlyList<string> missingKeys)
    {
        using var span = StartLogEventSpan("paths_configuration_missing", "Error");
        span?.SetTag("configuration_file", configurationFile);
        var missingKeysDisplay = string.Join(", ", missingKeys);
        span?.SetTag("missing_keys", missingKeysDisplay);

        logger.LogError(PathsConfigurationMissingEvent,
            "Runtime path configuration missing. configuration_file={configuration_file} missing_keys={missing_keys}",
            configurationFile, missingKeysDisplay);
    }

    /// <summary>
    /// The bound configuration supplies one or more configuration keys superseded by
    /// ADR-024's <c>Grimoire:Paths</c> regrouping, immediately before non-zero exit
    /// (FR-014/SC-010) — the rename would otherwise fail silently, since an unrecognized
    /// configuration key is normally just ignored.
    /// </summary>
    public static void LogConfigurationSuperseded(ILogger logger, IReadOnlyList<string> supersededKeys, IReadOnlyList<string> replacements)
    {
        using var span = StartLogEventSpan("paths_configuration_superseded", "Error");
        var supersededKeysDisplay = string.Join(", ", supersededKeys);
        var replacementsDisplay = string.Join(", ", replacements);
        span?.SetTag("superseded_keys", supersededKeysDisplay);
        span?.SetTag("replacements", replacementsDisplay);

        logger.LogError(PathsConfigurationSupersededEvent,
            "Runtime path configuration superseded. superseded_keys={superseded_keys} replacements={replacements}",
            supersededKeysDisplay, replacementsDisplay);
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
