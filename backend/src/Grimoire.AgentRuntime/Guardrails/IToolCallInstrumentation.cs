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
}

/// <summary>No-op default so hermetic tests that don't assert on telemetry don't need to wire an adapter.</summary>
public sealed class NullToolCallInstrumentation : IToolCallInstrumentation
{
    public static readonly NullToolCallInstrumentation Instance = new();

    private NullToolCallInstrumentation() { }

    public void RecordAllowed(string taskId, string tool, string target, int turn) { }
    public void RecordDenied(string taskId, string tool, string requestedTarget, string canonicalTarget, string reason, int turn) { }
}
