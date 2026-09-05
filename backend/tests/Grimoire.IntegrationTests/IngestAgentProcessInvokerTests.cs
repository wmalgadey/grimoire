using Grimoire.EvalRunner.Workspace;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #214: every EvalRunner capture pipeline used to report a failed sample's `StdErr` — which
/// the agent process never writes to — instead of the `reason` on its terminal `failed` NDJSON
/// event (stdout, per <c>RunEventEmitter</c> contract). This pins the one piece of new parsing
/// logic that fix depends on: <see cref="IngestAgentProcessInvoker.ParseFailureReason"/> reading
/// that field back out of raw stdout. A change to our own parsing is the only thing that can
/// turn this red — no process spawn, no provider — so it stays in the deterministic PR suite
/// alongside <see cref="EvalRunnerObservabilityTests"/>.
/// </summary>
public class IngestAgentProcessInvokerTests
{
    [Fact]
    public void ParseFailureReason_ReadsReasonFromTerminalFailedEvent()
    {
        var stdout =
            """
            {"type":"started","taskId":"probe","timestamp":"2026-08-29T20:50:00Z"}
            {"type":"failed","taskId":"probe","reason":"Model API error 403 (terminal): Authorization failed","timestamp":"2026-08-29T20:50:05Z"}
            """;

        Assert.Equal(
            "Model API error 403 (terminal): Authorization failed",
            IngestAgentProcessInvoker.ParseFailureReason(stdout));
    }

    [Fact]
    public void ParseFailureReason_ReturnsNullOnACompletedRun()
    {
        var stdout =
            """
            {"type":"started","taskId":"probe","timestamp":"2026-08-29T20:50:00Z"}
            {"type":"completed","taskId":"probe","summary":"done","timestamp":"2026-08-29T20:50:05Z"}
            """;

        Assert.Null(IngestAgentProcessInvoker.ParseFailureReason(stdout));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void ParseFailureReason_ReturnsNullWhenStdoutHasNoTerminalEvent(string stdout)
    {
        Assert.Null(IngestAgentProcessInvoker.ParseFailureReason(stdout));
    }
}
