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
    private static readonly EventId WikiIdentityFoundationResolvedEvent = new(44, "wiki_identity_foundation_resolved");

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
    /// 029-shared-foundation-prompt (T031, FR-018/SC-001): each time the effective foundation
    /// document is resolved for a dispatch. Deliberately does <b>not</b> start its own span
    /// (unlike every other event here) — plan.md ## Observability says this event lives inside
    /// the caller's existing active dispatch span, tagging it so the resolution correlates by
    /// <c>task_id</c> without inventing a span that would only ever have one child-free node.
    /// </summary>
    public static void LogFoundationResolved(ILogger logger, string agentId, string source, string resolvedPath, string sha256)
    {
        var current = Activity.Current;
        current?.SetTag("signal_type", "log");
        current?.SetTag("event_name", "wiki_identity_foundation_resolved");
        current?.SetTag("level", "Information");
        current?.SetTag("agent_id", agentId);
        current?.SetTag("source", source);
        current?.SetTag("resolved_path", resolvedPath);
        current?.SetTag("sha256", sha256);

        logger.LogInformation(WikiIdentityFoundationResolvedEvent,
            "Foundation document resolved. agent_id={agent_id} source={source} resolved_path={resolved_path} sha256={sha256}",
            agentId, source, resolvedPath, sha256);
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
