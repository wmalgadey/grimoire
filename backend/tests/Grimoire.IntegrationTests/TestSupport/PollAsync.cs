namespace Grimoire.IntegrationTests.TestSupport;

/// <summary>
/// Shared deterministic condition-based wait (ADR-021, spec 019-fast-test-tier FR-003/FR-004,
/// research.md R4). Formalizes the poll-loop pattern already used ~49 times across this suite
/// (a deadline-bounded loop polling a condition on a short interval) into one helper, so every
/// deterministic-tier wait completes as soon as its condition holds instead of sleeping a fixed
/// duration, and every timeout fails with a clear diagnostic (FR-004) instead of hanging.
///
/// This is the one call site <c>Grimoire.ArchTests.DeterministicTierNoFixedWaitRuleTests</c>
/// (contracts/deterministic-wait-rule.md) allow-lists for <see cref="Task.Delay(TimeSpan)"/> —
/// every other fixed/unconditional wait in a deterministic-tier test is either routed through
/// this helper or explicitly marked <c>[Trait("TimingDependent", "true")]</c>.
/// </summary>
public static class PollAsync
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Polls <paramref name="condition"/> on <paramref name="pollInterval"/> (default ~25ms,
    /// matching the suite's pre-existing ad hoc pattern) until it returns <see langword="true"/>
    /// or <paramref name="timeout"/> elapses, in which case the wait fails the test via
    /// <see cref="Xunit.Assert.Fail(string)"/> with <paramref name="onTimeoutMessage"/> (FR-004:
    /// a condition that never becomes true must fail with a bounded timeout and a diagnostic
    /// message, not hang the suite — Acceptance Scenario 4).
    /// </summary>
    public static async Task WaitAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        string onTimeoutMessage,
        TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? DefaultPollInterval;
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(interval).ConfigureAwait(false);
        }

        Xunit.Assert.Fail(onTimeoutMessage);
    }

    /// <summary>Synchronous-condition overload — wraps <paramref name="condition"/> in a completed <see cref="Task"/>.</summary>
    public static Task WaitAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string onTimeoutMessage,
        TimeSpan? pollInterval = null)
        => WaitAsync(() => Task.FromResult(condition()), timeout, onTimeoutMessage, pollInterval);
}
