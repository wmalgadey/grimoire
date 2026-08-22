using System.Diagnostics;

namespace Grimoire.AgentRuntime.Guardrails;

/// <summary>
/// Seam between the shared <see cref="GuardedToolExecutor"/> and each agent process's own
/// observability surface (ADR-011) — mirrors <c>IAgentLoopInstrumentation</c>'s rationale
/// for the tool-call span/metric/log-event triple, which also differs per agent
/// (<c>ingest_agent.tool_call</c> vs. <c>query_agent.tool_call</c>, etc.).
/// </summary>
public interface IToolCallInstrumentation
{
    void RecordAllowed(string taskId, string tool, string target, int turn);

    void RecordDenied(string taskId, string tool, string requestedTarget, string canonicalTarget, string reason, int turn);

    /// <summary>
    /// ADR-015 (012-query-synthesis-writes): a create-only write succeeded — a brand-new
    /// page was created under a create-only write-scope rule
    /// (contracts/query-write-scope-and-coordination.md, plan.md's
    /// <c>wiki.query.synthesis_page_created</c>/<c>wiki.query.synthesis_pages_created_total</c>
    /// Observability rows). Default no-op: only agents whose policy declares a
    /// create-only rule (Query today) override this; <see cref="NullToolCallInstrumentation"/>
    /// and Ingest (no create-only rule in its policy) never need to.
    /// </summary>
    void RecordCreateOnlyWriteSucceeded(string taskId, string path, int turn) { }

    /// <summary>
    /// ADR-015 (012-query-synthesis-writes): a write was rejected by the write-coordination
    /// guard's create-only existence check or its read-then-write compare-and-swap check —
    /// <paramref name="reason"/> is one of <c>create_only_target_exists</c> or
    /// <c>write_conflict_stale_read</c> (never <c>write_coordination_timeout</c>, which has
    /// its own <c>wiki.write_lock.timeout</c> signal, nor the pre-existing
    /// <c>out_of_scope</c>/<c>no_rule</c>/<c>traversal</c> policy-scope denials, which
    /// already have their own established signals via <see cref="RecordDenied"/>). Maps to
    /// plan.md's <c>wiki.write_conflict.rejected</c>/<c>wiki.write_conflict.rejections_total</c>
    /// Observability rows. Default no-op, mirroring <see cref="RecordCreateOnlyWriteSucceeded"/>.
    /// </summary>
    void RecordWriteConflictRejected(string taskId, string path, string reason, int turn) { }

    /// <summary>
    /// ADR-015 (012-query-synthesis-writes, T042): plan.md's <c>guardrails.acquire_write_lock</c>
    /// trace span, covering one write-coordination lock-acquisition attempt (the
    /// <see cref="Coordination.SharedFileWriteGuard.EvaluateWriteAsync"/> call). The caller
    /// (<see cref="GuardedToolExecutor"/>) sets the <c>path</c>/<c>outcome</c>/<c>wait_ms</c>
    /// attributes once the acquisition attempt completes and disposes the returned
    /// activity. Nests under whatever this agent's ambient activity is at acquisition
    /// time (in practice the active <c>*_agent.model_turn</c> span — the corresponding
    /// <c>*_agent.tool_call</c> span for this same write is, by the pre-existing
    /// RecordAllowed/RecordDenied contract, only created afterward once the write's final
    /// allow/deny decision is known, so it cannot yet be Activity.Current at acquisition
    /// time; see <c>QueryWriteLockObservabilityTests</c> for the documented parent-span note).
    /// Default no-op (<c>null</c>): only agents that construct their
    /// <see cref="GuardedToolExecutor"/> with a <c>writeLocksDir</c> (i.e. actually
    /// participate in write coordination) ever call this.
    /// </summary>
    Activity? StartAcquireWriteLockActivity(string taskId, string path, int turn) => null;

    /// <summary>
    /// ADR-015 (012-query-synthesis-writes, T042): plan.md's
    /// <c>wiki.write_lock.acquisitions_total</c> (<c>outcome=acquired|timeout</c>) counter
    /// and <c>wiki.write_lock.wait_seconds</c> histogram, plus — for
    /// <paramref name="outcome"/> <c>"timeout"</c> only — the <c>wiki.write_lock.timeout</c>
    /// WARN log event. Emitted once per write-coordination lock-acquisition attempt,
    /// mirroring <see cref="StartAcquireWriteLockActivity"/>'s trigger point.
    /// </summary>
    void RecordWriteLockAcquisition(string taskId, string path, string outcome, double waitSeconds, int turn) { }

    // ── 026-guarded-tool-surface (ADR-030/ADR-031): search, ranged read, batch, deletion ──
    // signals. All default no-op, mirroring the ADR-015 signals above: only Lint (the sole
    // agent that declares these tools, ADR-030 R6/ADR-031 R3) ever calls them.

    /// <summary>plan.md's <c>wiki.search.invocations_total</c> counter (labels: <c>agent</c>,
    /// <paramref name="outcome"/> ∈ completed|truncated|timed_out|denied|pattern_rejected)
    /// and, together, the source of the <c>wiki.search.matches_returned</c>/
    /// <c>wiki.search.files_scanned</c> histograms recorded via the two overloads below.
    /// Emitted once per <c>search_files</c> call.</summary>
    void RecordSearchInvocation(string taskId, string outcome, int matchesReturned, int filesScanned, int turn) { }

    /// <summary>plan.md's <c>wiki.search.truncated</c> WARN log event: the result cap was
    /// reached before every match was found.</summary>
    void LogSearchTruncated(string taskId, int patternLength, int cap, int turn) { }

    /// <summary>plan.md's <c>wiki.search.timed_out</c> WARN log event: the search time
    /// budget was exhausted mid-scan; results returned are partial, never empty.</summary>
    void LogSearchTimedOut(string taskId, double budgetMs, int filesScanned, int turn) { }

    /// <summary>plan.md's <c>wiki.search.pattern_rejected</c> WARN log event: the pattern
    /// was unsupported (e.g. lookaround) or exceeded the size bound (ADR-030 R2/R5).</summary>
    void LogSearchPatternRejected(string taskId, string reason, int patternLength, int turn) { }

    /// <summary>plan.md's <c>guardrails.search_scan</c> span. Caller sets
    /// <c>pattern_length</c>/<c>path_prefix</c>/<c>files_scanned</c>/<c>matches</c>/
    /// <c>truncated</c>/<c>outcome</c> once the scan completes.
    ///
    /// <b>Resolved during T029 implementation:</b> plan.md originally declared this a child
    /// of <c>*_agent.tool_call</c>, but that span is only created by
    /// <see cref="RecordAllowed"/>/<see cref="RecordDenied"/> — both create and dispose it
    /// internally, only after dispatch returns — so it is never live when a call to this
    /// method would need to parent to it. <c>AgentLoop</c>'s own turn loop confirms the
    /// actual ambient span during dispatch: its <c>*_agent.model_turn</c> activity is kept
    /// open with a <c>using</c> across the entire tool-dispatch call
    /// (<c>AgentLoop.cs</c>: "The span stays open across tool dispatch below so every
    /// per-agent tool-call span ... is a child of this model turn"). The actual, achievable
    /// parent is therefore <c>*_agent.model_turn</c> — matching <see cref="StartBatchActivity"/>'s
    /// already-correct declared parent — not <c>*_agent.tool_call</c>.
    /// </summary>
    Activity? StartSearchScanActivity(string taskId, int turn) => null;

    /// <summary>plan.md's <c>wiki.read.invocations_total</c> counter, labelled by
    /// <paramref name="shape"/> (<c>full</c>|<c>range</c>|<c>frontmatter</c>, ADR-030 R3) —
    /// the source for the SC-014 measurement (research.md D9). Emitted once per
    /// <c>read_file</c> call, including the pre-existing whole-file shape.</summary>
    void RecordReadInvocation(string taskId, string shape, int turn) { }

    /// <summary>plan.md's <c>wiki.batch.invocations_total</c> counter (labels: <c>agent</c>,
    /// <paramref name="outcome"/> ∈ completed|rejected_write|rejected_size, ADR-030 R4).
    /// Emitted once per <c>batch</c> call.</summary>
    void RecordBatchInvocation(string taskId, string outcome, int turn) { }

    /// <summary>plan.md's <c>wiki.batch.rejected</c> WARN log event: the batch contained a
    /// write/delete/nested batch, or exceeded the max call count — rejected wholesale
    /// before any member executed (ADR-030 R4).</summary>
    void LogBatchRejected(string taskId, string reason, int callCount, int turn) { }

    /// <summary>plan.md's <c>guardrails.batch</c> span, child of <c>*_agent.model_turn</c>
    /// (ADR-030 R4). Caller sets <c>call_count</c>/<c>denied_count</c>/<c>outcome</c> once
    /// the batch completes.</summary>
    Activity? StartBatchActivity(string taskId, int turn) => null;

    /// <summary>plan.md's <c>wiki.page.deletions_total</c> counter (labels: <c>agent</c>,
    /// <paramref name="outcome"/> ∈ applied|rolled_back, ADR-031 R3/R4).</summary>
    void RecordDeletion(string taskId, string outcome, int turn) { }

    /// <summary>plan.md's <c>wiki.page.deleted</c> INFO log event: a deletion applied
    /// through the guarded boundary.</summary>
    void LogPageDeleted(string taskId, string path, int turn) { }

    /// <summary>plan.md's <c>wiki.page.delete_rolled_back</c> WARN log event: a journaled
    /// deletion was restored during rollback (ADR-031 R4).</summary>
    void LogPageDeleteRolledBack(string taskId, string path, int turn) { }

    /// <summary>plan.md's <c>guardrails.delete_file</c> span. Caller sets <c>journaled</c>/
    /// <c>outcome</c> once the deletion (and any journal write) completes.
    ///
    /// Shares <see cref="StartSearchScanActivity"/>'s resolved parenting finding: the actual
    /// ambient span during dispatch is <c>*_agent.model_turn</c> (kept open by
    /// <c>AgentLoop</c> across the whole tool-dispatch call), not <c>*_agent.tool_call</c>
    /// (only live after <c>RecordAllowed</c>/<c>RecordDenied</c> return). Implemented as a
    /// child of <c>*_agent.model_turn</c> (T045; verified by
    /// <c>LintDeletionObservabilityTests</c>' T047 span-parenting test), matching
    /// <see cref="StartBatchActivity"/>'s already-correct declared parent.
    /// </summary>
    Activity? StartDeleteFileActivity(string taskId, string path, int turn) => null;
}

/// <summary>No-op default so hermetic tests that don't assert on telemetry don't need to wire an adapter.</summary>
public sealed class NullToolCallInstrumentation : IToolCallInstrumentation
{
    public static readonly NullToolCallInstrumentation Instance = new();

    private NullToolCallInstrumentation() { }

    public void RecordAllowed(string taskId, string tool, string target, int turn) { }
    public void RecordDenied(string taskId, string tool, string requestedTarget, string canonicalTarget, string reason, int turn) { }
}
