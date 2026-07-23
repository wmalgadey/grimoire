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
}

/// <summary>No-op default so hermetic tests that don't assert on telemetry don't need to wire an adapter.</summary>
public sealed class NullToolCallInstrumentation : IToolCallInstrumentation
{
    public static readonly NullToolCallInstrumentation Instance = new();

    private NullToolCallInstrumentation() { }

    public void RecordAllowed(string taskId, string tool, string target, int turn) { }
    public void RecordDenied(string taskId, string tool, string requestedTarget, string canonicalTarget, string reason, int turn) { }
}
