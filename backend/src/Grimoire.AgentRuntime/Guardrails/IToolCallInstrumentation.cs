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
}

/// <summary>No-op default so hermetic tests that don't assert on telemetry don't need to wire an adapter.</summary>
public sealed class NullToolCallInstrumentation : IToolCallInstrumentation
{
    public static readonly NullToolCallInstrumentation Instance = new();

    private NullToolCallInstrumentation() { }

    public void RecordAllowed(string taskId, string tool, string target, int turn) { }
    public void RecordDenied(string taskId, string tool, string requestedTarget, string canonicalTarget, string reason, int turn) { }
}
