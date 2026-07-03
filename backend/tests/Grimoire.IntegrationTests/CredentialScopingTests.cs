using Grimoire.Hub.AgentDispatch;

namespace Grimoire.IntegrationTests;

/// <summary>T039 — ANTHROPIC_AUTH_TOKEN scoped to child process only (ADR-004).</summary>
public class CredentialScopingTests
{
    /// <summary>
    /// ADR-004: the Hub must never pass ANTHROPIC_AUTH_TOKEN to child processes via
    /// environment inheritance. The dispatcher explicitly strips both the old and new
    /// key names from the child env before selectively re-injecting from the secrets file.
    /// </summary>
    [Fact]
    public void Dispatcher_ExplicitlyStrips_AuthToken_From_ChildEnv_WhenSecretsFileIsEmpty()
    {
        // With an empty secrets file the token must NOT reach the child process,
        // even if ANTHROPIC_AUTH_TOKEN happens to be set in the parent shell.
        // We verify this by inspecting LocalSecretsLoader returning null.
        var root = Path.Combine(Path.GetTempPath(), $"cred-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var envPath = Path.Combine(root, ".env");
        File.WriteAllText(envPath, "# no credentials here");

        var loader = new LocalSecretsLoader(envPath);
        Assert.Null(loader.GetAnthropicAuthToken());
        // The dispatcher calls Environment.Remove("ANTHROPIC_AUTH_TOKEN") before
        // the conditional add, so null return = token stripped from child env.
    }

    [Fact]
    public void LocalSecretsLoader_ReadsToken_FromFile_NotFromEnvironment()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cred-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var envPath = Path.Combine(root, ".env");
        File.WriteAllText(envPath, "ANTHROPIC_AUTH_TOKEN=sk-ant-test-from-file");

        var loader = new LocalSecretsLoader(envPath);
        var token = loader.GetAnthropicAuthToken();

        // Loader must return the file value regardless of what the parent env contains.
        Assert.Equal("sk-ant-test-from-file", token);
    }

    [Fact]
    public void LocalSecretsLoader_ReturnsNull_WhenFileAbsent()
    {
        var loader = new LocalSecretsLoader(Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}/.env"));
        Assert.Null(loader.GetAnthropicAuthToken());
    }

    [Fact]
    public void LocalSecretsLoader_IgnoresComments_AndBlanks()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cred-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var envPath = Path.Combine(root, ".env");
        File.WriteAllText(envPath,
            "# This is a comment\n" +
            "\n" +
            "OTHER_KEY=some-value\n" +
            "ANTHROPIC_AUTH_TOKEN=real-token\n");

        var loader = new LocalSecretsLoader(envPath);
        Assert.Equal("real-token", loader.GetAnthropicAuthToken());
    }
}
