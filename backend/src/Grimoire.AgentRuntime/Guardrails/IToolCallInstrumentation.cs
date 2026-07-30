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
    /// time; see <c>WriteLockObservabilityTests</c> for the documented parent-span note).
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
}

/// <summary>No-op default so hermetic tests that don't assert on telemetry don't need to wire an adapter.</summary>
public sealed class NullToolCallInstrumentation : IToolCallInstrumentation
{
    public static readonly NullToolCallInstrumentation Instance = new();

    private NullToolCallInstrumentation() { }

    public void RecordAllowed(string taskId, string tool, string target, int turn) { }
    public void RecordDenied(string taskId, string tool, string requestedTarget, string canonicalTarget, string reason, int turn) { }
}
