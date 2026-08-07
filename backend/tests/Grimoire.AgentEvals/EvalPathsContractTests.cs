using Grimoire.EvalRunner.Workspace;

namespace Grimoire.AgentEvals;

/// <summary>
/// T043 (ADR-022) — <see cref="EvalPaths.RecordingsRoot"/> resolves under the test
/// project (SC-009), the eval runner's CLI parser has no <c>--recordings-root</c> switch
/// to recognize (SC-009), and <see cref="EvalPaths.LocalEnvPath"/> independently computes
/// the same repo-root-anchored <c>.env</c> location <c>GrimoirePathResolver</c>'s
/// <c>SecretsFilePath</c> resolves to (SC-011) — proven without depending on
/// <c>Grimoire.Hub</c> at all, since an eval run must stay independent of hub
/// configuration (FR-016-FR-019).
/// </summary>
[Trait("Tier", "Fast")]
public class EvalPathsContractTests
{
    [Fact]
    public void RecordingsRoot_ResolvesUnderTheAgentEvalsTestProject_NotUnderData()
    {
        var paths = EvalPaths.Discover();

        var expected = Path.Combine(
            paths.RepoRoot, "backend", "tests", "Grimoire.AgentEvals", "Fixtures", "recordings");
        Assert.Equal(expected, paths.RecordingsRoot);
        Assert.True(Directory.Exists(paths.RecordingsRoot));
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}data{Path.DirectorySeparatorChar}", paths.RecordingsRoot, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalEnvPath_ResolvesToDotEnvAtTheRepositoryRoot()
    {
        var paths = EvalPaths.Discover();

        Assert.Equal(Path.Combine(paths.RepoRoot, ".env"), paths.LocalEnvPath);
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("replay")]
    public void CliParser_HasNoRecordingsRootSwitch_ItIsSilentlyIgnored(string subcommand)
    {
        // No --recordings-root switch exists any more (ADR-022/FR-016/SC-009):
        // CliOptions.Parse recognizes only --scenario/--samples/--summary, so passing it
        // has no effect on the parsed result — proving there is nothing left to bind it
        // to, rather than asserting a parse failure the parser was never designed to raise.
        var (parsedSubcommand, options) = global::CliOptions.Parse(
            [subcommand, "--recordings-root", "/some/other/path", "--scenario", "s1"]);

        Assert.Equal(subcommand, parsedSubcommand);
        Assert.Equal(["s1"], options.Scenarios);
        Assert.Null(options.Samples);
        Assert.Null(options.SummaryPath);
    }
}
