using Grimoire.AgentRuntime.Core;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// The parse-or-default rule behind <c>GRIMOIRE_LINT_SPEND_CAP</c>. The cap's *effect* is
/// already pinned by <see cref="AgentLoopCapTests"/> against a scripted fake client; what
/// is asserted here is only the part this variable adds — which string yields which cap.
/// <para>
/// It matters because the failure mode is silent: a cap that is quietly ignored looks
/// exactly like a cap that was honoured until a run dies at a number the operator did not
/// choose. Every case below is decided by Grimoire's own source, so each can fail from a
/// change to it alone.
/// </para>
/// </summary>
public class LintSpendCapTests
{
    [Fact]
    public void AConfiguredValue_BecomesTheCap()
        => Assert.Equal(5_000_000, LintSpendCap.Resolve("5000000"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("1_000_000")]
    [InlineData("1000000.5")]
    [InlineData("0")]
    [InlineData("-1")]
    public void AnUnusableValue_FallsBackToTheLoopDefault(string? raw)
        => Assert.Equal(AgentLoop.DefaultSpendTokenCap, LintSpendCap.Resolve(raw));
}
