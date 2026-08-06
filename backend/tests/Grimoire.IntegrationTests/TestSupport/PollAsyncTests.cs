using System.Diagnostics;

namespace Grimoire.IntegrationTests.TestSupport;

/// <summary>
/// T013 (019-fast-test-tier, US2) — Acceptance Scenario 4 / FR-004: a condition-based wait
/// whose condition never becomes true must fail with a clear timeout diagnosis rather than
/// hanging indefinitely, and a wait whose condition holds early must return as soon as it
/// does rather than sleeping out the full timeout (FR-003).
/// </summary>
public class PollAsyncTests
{
    [Fact]
    public async Task WaitAsync_ConditionNeverTrue_FailsWithBoundedTimeoutAndDiagnosticMessage()
    {
        var stopwatch = Stopwatch.StartNew();

        var exception = await Record.ExceptionAsync(() => PollAsync.WaitAsync(
            () => false,
            TimeSpan.FromMilliseconds(150),
            "the condition never became true — this is the expected diagnosis",
            pollInterval: TimeSpan.FromMilliseconds(10)));

        stopwatch.Stop();

        Assert.NotNull(exception);
        Assert.Contains("the condition never became true", exception.Message, StringComparison.Ordinal);
        // Bounded: fails shortly after the timeout, never hangs (generous multiplier absorbs
        // scheduling jitter under full-suite parallel load).
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Expected the wait to fail near its 150ms timeout, not hang; took {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task WaitAsync_ConditionBecomesTrueEarly_ReturnsAsSoonAsItDoes_WithoutSleepingTheFullTimeout()
    {
        var stopwatch = Stopwatch.StartNew();

        await PollAsync.WaitAsync(
            () => stopwatch.Elapsed >= TimeSpan.FromMilliseconds(40),
            TimeSpan.FromSeconds(5),
            "unreachable — the condition holds well before the 5s timeout",
            pollInterval: TimeSpan.FromMilliseconds(10));

        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"Expected early completion once the condition held, not a wait out to the 5s timeout; took {stopwatch.ElapsedMilliseconds}ms.");
    }
}
