using Grimoire.EvalRunner.Providers;

namespace Grimoire.AgentEvals;

/// <summary>
/// Hermetic tests for the `capture`-only `data/.env` convenience loader. Uses
/// process-uniquely-named fake variables (never real provider names) and always restores
/// the prior environment state, since <see cref="LocalEnvFile.ApplyIfPresent"/> mutates the
/// real process environment.
/// </summary>
[Trait("Tier", "Fast")]
public class LocalEnvFileTests
{
    [Fact]
    public void ApplyIfPresent_FileAbsent_IsNoOp()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"grimoire-localenv-missing-{Guid.NewGuid():N}.env");

        var exception = Record.Exception(() => LocalEnvFile.ApplyIfPresent(missingPath));

        Assert.Null(exception);
    }

    [Fact]
    public void ApplyIfPresent_UnsetVariable_IsPopulatedFromFile()
    {
        const string varName = "GRIMOIRE_EVAL_TEST_LOCALENV_UNSET";
        var path = WriteTempEnvFile($"{varName}=from-file\n");

        try
        {
            Environment.SetEnvironmentVariable(varName, null);

            LocalEnvFile.ApplyIfPresent(path);

            Assert.Equal("from-file", Environment.GetEnvironmentVariable(varName));
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
            File.Delete(path);
        }
    }

    [Fact]
    public void ApplyIfPresent_AlreadySetVariable_IsNeverOverridden()
    {
        const string varName = "GRIMOIRE_EVAL_TEST_LOCALENV_ALREADY_SET";
        var path = WriteTempEnvFile($"{varName}=from-file\n");

        try
        {
            Environment.SetEnvironmentVariable(varName, "from-shell");

            LocalEnvFile.ApplyIfPresent(path);

            Assert.Equal("from-shell", Environment.GetEnvironmentVariable(varName));
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
            File.Delete(path);
        }
    }

    [Fact]
    public void ApplyIfPresent_CommentsAndBlankLines_AreIgnored()
    {
        const string varName = "GRIMOIRE_EVAL_TEST_LOCALENV_QUOTED";
        var path = WriteTempEnvFile($"# a comment\n\n{varName}=\"quoted-value\"\n");

        try
        {
            Environment.SetEnvironmentVariable(varName, null);

            LocalEnvFile.ApplyIfPresent(path);

            Assert.Equal("quoted-value", Environment.GetEnvironmentVariable(varName));
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
            File.Delete(path);
        }
    }

    private static string WriteTempEnvFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"grimoire-localenv-{Guid.NewGuid():N}.env");
        File.WriteAllText(path, content);
        return path;
    }
}
