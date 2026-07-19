using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;

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
        // Simulate a parent environment that contains ANTHROPIC_AUTH_TOKEN. Even when the
        // token is present in the parent env, the dispatcher must remove it from the child
        // env copy (ProcessStartInfo.Environment) and only re-inject from the secrets file.
        var parentEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANTHROPIC_AUTH_TOKEN"] = "sk-ant-inherited-from-parent",
            ["ANTHROPIC_API_KEY"] = "sk-legacy-inherited-from-parent",
            ["PATH"] = "/usr/bin",
        };

        // authToken=null simulates LocalSecretsLoader returning null (empty/absent secrets file).
        var childEnv = AgentProcessHost.BuildChildEnvironment(parentEnv, authToken: null);

        Assert.False(childEnv.ContainsKey("ANTHROPIC_AUTH_TOKEN"),
            "ANTHROPIC_AUTH_TOKEN must not be present in child env when secrets file returns null.");
        Assert.False(childEnv.ContainsKey("ANTHROPIC_API_KEY"),
            "ANTHROPIC_API_KEY must not be present in child env (legacy key stripped unconditionally).");
    }

    [Fact]
    public void Dispatcher_InjectsAuthToken_IntoChildEnv_WhenSecretsFileProvides()
    {
        var parentEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = "/usr/bin",
        };

        var childEnv = AgentProcessHost.BuildChildEnvironment(parentEnv, authToken: "sk-ant-from-file");

        Assert.Equal("sk-ant-from-file", childEnv["ANTHROPIC_AUTH_TOKEN"]);
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
