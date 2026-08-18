using System.Diagnostics;
using System.Diagnostics.Metrics;
using Grimoire.AgentRuntime.Guardrails;
using Microsoft.Extensions.Logging;

namespace Grimoire.AgentRuntime.WikiLog;

/// <summary>The coverage outcome of one run (data-model.md §4).</summary>
public enum WikiLogCoverageOutcome
{
    /// <summary>The run was allowed no wiki-content writes. Nothing to log; no signal.</summary>
    NoChange,

    /// <summary>The activity log is among the run's written paths. No signal.</summary>
    Logged,

    /// <summary>
    /// The run changed wiki content and did not write the activity log — the one
    /// diagnostic the deleted backstop carried that nothing else does.
    /// </summary>
    NotLogged,
}

/// <summary>
/// 025-agent-owned-log (ADR-028, FR-012a): the write-free replacement for the deleted
/// <c>WikiLogAppender</c> backstop. Invoked once at run end by each writing agent process,
/// it reports the combination the operator wants to know about — an agent changed the wiki
/// and did not log it — <em>without</em> putting harness prose into wiki content.
///
/// Two properties are load-bearing:
/// <list type="bullet">
/// <item>It performs <b>no I/O at all</b>. The determination is set arithmetic over
/// <see cref="GuardedToolExecutor"/>'s own record of writes it allowed, never a read of
/// the log's content. That is what lets Boundary Rule BR-1 forbid every filesystem-write
/// API in this namespace, and what makes SC-009's "never results in a write to the wiki"
/// true by construction rather than by discipline.</item>
/// <item>It makes <b>no judgment</b> about whether an entry <em>ought</em> to have been
/// written — that is agent judgment under the instruction files (Constitution Principle
/// V), sampled by SC-005–SC-007. This only reports a mechanical fact about the harness's
/// own bookkeeping.</item>
/// </list>
///
/// Like every other shared <c>Grimoire.AgentRuntime</c> telemetry component, it takes the
/// calling process's frozen <see cref="ActivitySource"/>/<see cref="Meter"/> rather than
/// owning a static telemetry identity (ADR-005/ADR-013).
/// </summary>
public sealed class WikiLogCoverageObserver
{
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly ILogger _logger;

    public WikiLogCoverageObserver(ActivitySource activitySource, Meter meter, ILogger logger)
    {
        _activitySource = activitySource;
        _meter = meter;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates the run's coverage once and emits the <c>wiki_log.coverage_check</c> span
    /// (always) plus <c>wiki.log.change_not_logged</c> and
    /// <c>wiki.log.unlogged_change_total</c> (only on
    /// <see cref="WikiLogCoverageOutcome.NotLogged"/>). Returns the outcome so callers and
    /// tests can assert it directly.
    /// </summary>
    /// <param name="type"><c>ingest</c> or <c>query</c> — the agent process emitting the signal.</param>
    /// <param name="taskIdOrRunId">Task id (Ingest) or turn id (Query).</param>
    public WikiLogCoverageOutcome Observe(GuardedToolExecutor executor, string type, string taskIdOrRunId)
    {
        ArgumentNullException.ThrowIfNull(executor);

        var wikiContentWrites = executor.WikiContentWrites.Count;
        var outcome = wikiContentWrites == 0
            ? WikiLogCoverageOutcome.NoChange
            : executor.ActivityLogWritten
                ? WikiLogCoverageOutcome.Logged
                : WikiLogCoverageOutcome.NotLogged;

        using var span = _activitySource.StartActivity("wiki_log.coverage_check");
        span?.SetTag("type", type);
        span?.SetTag("task_id_or_run_id", taskIdOrRunId);
        span?.SetTag("wiki_content_writes", wikiContentWrites);
        span?.SetTag("outcome", ToAttributeValue(outcome));

        if (outcome == WikiLogCoverageOutcome.NotLogged)
        {
            // Started inside the coverage-check span, so the log-event span is its child.
            WikiLogEvents.LogChangeNotLogged(_logger, _activitySource, type, taskIdOrRunId, wikiContentWrites);
            WikiLogMetrics.RecordUnloggedChange(_meter, type);
        }

        return outcome;
    }

    private static string ToAttributeValue(WikiLogCoverageOutcome outcome) => outcome switch
    {
        WikiLogCoverageOutcome.NoChange => "no_change",
        WikiLogCoverageOutcome.Logged => "logged",
        WikiLogCoverageOutcome.NotLogged => "not_logged",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
