using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// Shared fixture setup for the path-configuration hermetic test suite
/// (specs/005-content-root-config): seeds the required-input files a
/// <see cref="GrimoirePathResolver"/> validation pass needs to succeed, under the
/// documented default layout beneath a given base directory.
/// </summary>
internal static class PathConfigurationTestHelpers
{
    public const string ValidPolicyJson =
        """
        {
          "version": 1,
          "defaultDecision": "deny",
          "read": [{"pathPrefix": "pages/"}],
          "write": [{"pathPrefix": "pages/"}]
        }
        """;

    /// <summary>
    /// Seeds secrets file, the three instruction files, and an agent-worker stub under
    /// <paramref name="baseDir"/>'s default data layout, and returns options with
    /// <c>BaseDir</c> and <c>AgentWorker</c> set (AgentWorker cannot default to the test
    /// host's own install directory — tests never spawn a real process).
    /// </summary>
    public static GrimoirePathOptions SeedRequiredInputs(string baseDir)
    {
        var dataDir = Path.Combine(baseDir, GrimoirePathOptions.DefaultDataDirName);
        var instructionsDir = Path.Combine(dataDir, GrimoirePathOptions.DefaultInstructionsDirRelativePath);
        var secretsFile = Path.Combine(dataDir, GrimoirePathOptions.DefaultSecretsFileName);
        var agentWorker = Path.Combine(baseDir, "agent-worker-stub.dll");

        Directory.CreateDirectory(instructionsDir);
        File.WriteAllText(Path.Combine(instructionsDir, "system-prompt.md"), "# Test system prompt\nRules.\n");
        File.WriteAllText(Path.Combine(instructionsDir, "default-user-prompt.md"), "Please integrate the source.\n");
        File.WriteAllText(Path.Combine(instructionsDir, "policy.json"), ValidPolicyJson);
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(secretsFile, "ANTHROPIC_AUTH_TOKEN=test-token\n");
        File.WriteAllText(agentWorker, "stub");

        return new GrimoirePathOptions
        {
            BaseDir = baseDir,
            AgentWorker = agentWorker,
        };
    }
}
