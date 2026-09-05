using System.Diagnostics.Metrics;
using Grimoire.AgentRuntime.RunEvents;

namespace Grimoire.LintAgent;

/// <summary>
/// Agent-process-side business metrics. The guarded tool executor and model loop run
/// inside this process, not the Hub, so their metrics are emitted from here — mirrors
/// <c>Grimoire.QueryAgent.QueryAgentMetrics</c>. The Hub-side <c>wiki.lint.*</c> metrics
/// declared in plan.md ## Observability (runs_total, findings_total,
/// inbound_links_refreshed_total, triggers_rejected_total) are emitted from
/// <c>Grimoire.Hub.LintDispatch</c> instead, since those are dispatch-lifecycle/
/// report-derived facts the Hub already owns.
/// </summary>
public static class LintAgentMetrics
{
    internal static readonly Meter Meter = new("Grimoire.LintAgent", "1.0.0");

    private static readonly Counter<long> _toolCallsTotal =
        Meter.CreateCounter<long>("lint.tool_calls_total",
            description: "Guarded tool calls dispatched by the Lint agent");

    /// <summary>ADR-015/ADR-016: reuses the shared write-conflict signal (plan.md's
    /// Observability note) — writes rejected by the write-coordination guard's
    /// existence/compare-and-swap/frontmatter-body checks.</summary>
    private static readonly Counter<long> _writeConflictRejectionsTotal =
        Meter.CreateCounter<long>("wiki.write_conflict.rejections_total",
            description: "Writes rejected by compare-and-swap (stale read), create-only, or frontmatter-only checks");

    private static readonly Counter<long> _writeLockAcquisitionsTotal =
        Meter.CreateCounter<long>("wiki.write_lock.acquisitions_total",
            description: "Write-coordination lock acquisition attempts");

    private static readonly Histogram<double> _writeLockWaitSeconds =
        Meter.CreateHistogram<double>("wiki.write_lock.wait_seconds",
            unit: "s",
            description: "Time spent waiting to acquire a write-coordination lock");

    // ── 026-guarded-tool-surface (ADR-030/ADR-031) ──────────────────────────────────────

    private static readonly Counter<long> _searchInvocationsTotal =
        Meter.CreateCounter<long>("wiki.search.invocations_total",
            description: "One per search_files call");

    private static readonly Histogram<long> _searchMatchesReturned =
        Meter.CreateHistogram<long>("wiki.search.matches_returned",
            description: "Matches returned per search");

    private static readonly Histogram<long> _searchFilesScanned =
        Meter.CreateHistogram<long>("wiki.search.files_scanned",
            description: "Files opened per search");

    private static readonly Counter<long> _readInvocationsTotal =
        Meter.CreateCounter<long>("wiki.read.invocations_total",
            description: "Reads by shape, to show ranged reads displacing whole-page reads");

    private static readonly Counter<long> _batchInvocationsTotal =
        Meter.CreateCounter<long>("wiki.batch.invocations_total",
            description: "One per batch call");

    private static readonly Counter<long> _pageDeletionsTotal =
        Meter.CreateCounter<long>("wiki.page.deletions_total",
            description: "Pages deleted through the guarded boundary");

    // ── 028-lint-at-scale (US2, FR-003, plan.md ## Observability) ───────────────────────
    // wiki.lint.runs_total already exists Hub-side (HubMetrics.cs), labeled `outcome` and
    // counting one increment per terminal run regardless of coverage — reusing that exact
    // name here for a second, differently-labeled counter would double-count runs under
    // one metric name. Named distinctly instead; plan.md's Observability table is corrected
    // to match (see this feature's Final Phase completeness audit).

    private static readonly Histogram<double> _coverageRatio =
        Meter.CreateHistogram<double>("wiki.lint.coverage_ratio",
            description: "pages_considered / pages_total for one completed Lint run");

    private static readonly Counter<long> _coverageRunsTotal =
        Meter.CreateCounter<long>("wiki.lint.coverage_runs_total",
            description: "Completed Lint runs, by coverage status");

    public static void RecordCoverage(WikiCoverage coverage)
    {
        var ratio = coverage.PagesTotal > 0 ? (double)coverage.PagesConsidered / coverage.PagesTotal : 0.0;
        _coverageRatio.Record(ratio, new KeyValuePair<string, object?>("agent", "lint"));
        _coverageRunsTotal.Add(1,
            new KeyValuePair<string, object?>("agent", "lint"),
            new KeyValuePair<string, object?>("coverage_status", coverage.Status));
    }

    public static void RecordSearchInvocation(string outcome, int matchesReturned, int filesScanned)
    {
        _searchInvocationsTotal.Add(1,
            new KeyValuePair<string, object?>("agent", "lint"),
            new KeyValuePair<string, object?>("outcome", outcome));
        _searchMatchesReturned.Record(matchesReturned, new KeyValuePair<string, object?>("agent", "lint"));
        _searchFilesScanned.Record(filesScanned, new KeyValuePair<string, object?>("agent", "lint"));
    }

    public static void RecordReadInvocation(string shape)
    {
        _readInvocationsTotal.Add(1,
            new KeyValuePair<string, object?>("agent", "lint"),
            new KeyValuePair<string, object?>("shape", shape));
    }

    public static void RecordBatchInvocation(string outcome)
    {
        _batchInvocationsTotal.Add(1,
            new KeyValuePair<string, object?>("agent", "lint"),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void RecordDeletion(string outcome)
    {
        _pageDeletionsTotal.Add(1,
            new KeyValuePair<string, object?>("agent", "lint"),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void RecordToolCall(string tool, string decision)
    {
        _toolCallsTotal.Add(1,
            new KeyValuePair<string, object?>("tool", tool),
            new KeyValuePair<string, object?>("decision", decision));
    }

    public static void RecordWriteConflictRejected(string reason)
        => _writeConflictRejectionsTotal.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public static void RecordWriteLockAcquisition(string outcome, double waitSeconds)
    {
        _writeLockAcquisitionsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        _writeLockWaitSeconds.Record(waitSeconds);
    }
}
