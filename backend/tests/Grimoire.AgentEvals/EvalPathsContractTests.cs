using Grimoire.EvalRunner.Workspace;

namespace Grimoire.AgentEvals;

/// <summary>
/// T043 (ADR-022) — <see cref="EvalPaths.RecordingsRoot"/> resolves under the test
/// project, not under <c>data</c> (SC-009), and <see cref="EvalPaths.LocalEnvPath"/>
/// independently computes the same repo-root-anchored <c>.env</c> location
/// <c>GrimoirePathResolver</c>'s <c>SecretsFilePath</c> resolves to (SC-011) — proven
/// without depending on <c>Grimoire.Hub</c> at all, since an eval run must stay
/// independent of hub configuration (FR-016-FR-019).
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
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}data{Path.DirectorySeparatorChar}", paths.RecordingsRoot, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalEnvPath_ResolvesToDotEnvAtTheRepositoryRoot()
    {
        var paths = EvalPaths.Discover();

        Assert.Equal(Path.Combine(paths.RepoRoot, ".env"), paths.LocalEnvPath);
    }
}
