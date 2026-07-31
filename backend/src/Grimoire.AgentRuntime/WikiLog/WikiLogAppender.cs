using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentRuntime.WikiLog;

/// <summary>
/// Harness backstop for <c>log.md</c> (generalized from the Ingest-only
/// <c>Grimoire.IngestAgent.IngestLog.IngestLogAppender</c> —
/// 014-wiki-storage-restructure R5/FR-010 — into a shared component every agent process
/// uses). On success the agent is expected to append its own log entry via
/// <c>write_file</c>; this appender verifies that an entry mentioning the run's
/// correlation id (task id for Ingest, turn id for Query) already exists — if absent, and
/// always on failure, it appends a minimal, harness-generated factual entry in the same
/// <c>## [DATE] TYPE | SUMMARY</c> heading-plus-paragraph shape every agent-authored entry
/// uses (contracts/log-and-catalog-entry-format.md), and emits
/// <c>wiki.log.backstop_appended</c> (WARN).
///
/// <c>log.md</c> is structurally append-only (FR-011, ADR-017): every entry — agent- or
/// backstop-written — is added after existing content, never inserted above it. This
/// appender always writes at the end of the file by construction.
///
/// Constructed once per agent-process run with that process's own frozen
/// <see cref="ActivitySource"/>/<see cref="Meter"/> identities (ADR-005/ADR-013) — this
/// type owns neither itself; both are supplied by the caller (see
/// <see cref="WikiLogEvents"/>/<see cref="WikiLogMetrics"/> doc comments for why a shared
/// `Grimoire.AgentRuntime` component cannot own a static identity the way a per-agent
/// `*AgentMetrics`/`*AgentTracing` class does).
/// </summary>
public sealed class WikiLogAppender
{
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly ILogger<WikiLogAppender> _logger;

    public WikiLogAppender(ActivitySource activitySource, Meter meter, ILogger<WikiLogAppender>? logger = null)
    {
        _activitySource = activitySource;
        _meter = meter;
        _logger = logger ?? NullLogger<WikiLogAppender>.Instance;
    }

    /// <summary>
    /// Ensures a log entry mentioning <paramref name="correlationId"/> exists in
    /// <paramref name="logPath"/>. Always appends on failure (<paramref name="forceAppend"/>
    /// <c>true</c>); on success only appends if no existing content already mentions the
    /// correlation id — i.e. the agent's own entry (which, per instruction-file
    /// convention, always names its task/turn reference in the paragraph) was found.
    /// </summary>
    public async Task EnsureLogEntryAsync(
        string logPath,
        string type,
        string outcome,
        string sourceRef,
        string correlationId,
        bool forceAppend,
        CancellationToken cancellationToken)
    {
        var backstopNeeded = forceAppend;

        if (!backstopNeeded)
        {
            if (File.Exists(logPath))
            {
                var logContent = await File.ReadAllTextAsync(logPath, cancellationToken);
                backstopNeeded = !logContent.Contains(correlationId, StringComparison.Ordinal);
            }
            else
            {
                // log.md doesn't exist yet — backstop needed.
                backstopNeeded = true;
            }
        }

        if (!backstopNeeded)
            return;

        const string detail = "No agent-authored entry was found for this run; the harness recorded this fallback entry instead.";
        await AppendAsync(logPath, type, outcome, sourceRef, detail, correlationId, cancellationToken);
    }

    /// <summary>
    /// Unconditionally appends one backstop entry in the shared heading-plus-paragraph
    /// shape. Used by <see cref="EnsureLogEntryAsync"/>, and directly by any other
    /// harness-side caller that needs the same conforming shape without the
    /// "does an entry already exist" check (e.g. crash-reconciliation paths).
    /// </summary>
    public async Task AppendAsync(
        string logPath,
        string type,
        string outcome,
        string sourceRef,
        string detail,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var entry = BuildEntry(type, outcome, sourceRef, detail, correlationId);

        using var span = _activitySource.StartActivity("wiki_log.backstop_append");
        span?.SetTag("type", type);
        span?.SetTag("task_id_or_run_id", correlationId);
        span?.SetTag("outcome", outcome);

        EnsureParentDirectory(logPath);
        await File.AppendAllTextAsync(logPath, entry, cancellationToken);

        WikiLogEvents.LogBackstopAppended(_logger, _activitySource, type, correlationId, outcome);
        WikiLogMetrics.RecordBackstopAppended(_meter, type);
    }

    /// <summary>
    /// Builds one conforming <c>## [DATE] TYPE | SUMMARY</c> heading + paragraph entry
    /// (contracts/log-and-catalog-entry-format.md) — always satisfies ADR-017's
    /// structural check by construction (heading pattern, non-blank paragraph
    /// immediately following). Detail that used to live in the heading (source
    /// reference, task/conversation link, outcome) lives in the paragraph instead.
    /// </summary>
    internal static string BuildEntry(string type, string outcome, string sourceRef, string detail, string correlationId)
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var summary = $"{outcome} (harness backstop)";
        var paragraph =
            $"Harness backstop entry for source \"{sourceRef}\", outcome \"{outcome}\". {detail} Ref: {correlationId}.";

        return $"## [{date}] {type} | {summary}{Environment.NewLine}{Environment.NewLine}{paragraph}{Environment.NewLine}";
    }

    private static void EnsureParentDirectory(string logPath)
    {
        var logDir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(logDir))
        {
            Directory.CreateDirectory(logDir);
        }
    }
}
