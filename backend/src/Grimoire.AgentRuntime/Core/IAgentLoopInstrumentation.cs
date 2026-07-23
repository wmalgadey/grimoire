using System.Diagnostics;

namespace Grimoire.AgentRuntime.Core;

/// <summary>
/// Seam between the shared <see cref="AgentLoop"/> and each agent process's own
/// observability surface (ADR-011): Grimoire.IngestAgent and Grimoire.QueryAgent each
/// have their own metric names, span names, and structured log events (plan.md
/// Observability tables differ per agent), so the loop itself stays agnostic and simply
/// calls back into whichever implementation its composition root supplied.
/// </summary>
public interface IAgentLoopInstrumentation
{
    /// <summary>
    /// Starts the per-turn model-turn span (parent for any tool-call spans dispatched
    /// during that turn), with <c>task_id</c>/<c>turn</c> already tagged.
    /// </summary>
    Activity? StartModelTurnActivity(string taskId, int turn);

    void RecordAgentTurns(int turns, string outcome);

    void RecordModelTokens(int inputTokens, int outputTokens);

    void RecordModelToolRequests(int toolRequestCount, ModelStopReason stopReason);

    void RecordNoToolTurn(ModelStopReason stopReason, string outcome);
}

/// <summary>No-op default so hermetic tests that don't assert on telemetry don't need to wire an adapter.</summary>
public sealed class NullAgentLoopInstrumentation : IAgentLoopInstrumentation
{
    public static readonly NullAgentLoopInstrumentation Instance = new();

    private NullAgentLoopInstrumentation() { }

    public Activity? StartModelTurnActivity(string taskId, int turn) => null;
    public void RecordAgentTurns(int turns, string outcome) { }
    public void RecordModelTokens(int inputTokens, int outputTokens) { }
    public void RecordModelToolRequests(int toolRequestCount, ModelStopReason stopReason) { }
    public void RecordNoToolTurn(ModelStopReason stopReason, string outcome) { }
}
