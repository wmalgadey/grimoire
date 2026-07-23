using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>Computed locations of the files <see cref="PathConfigurationTestHelpers"/> seeds, for tests that corrupt one at a time.</summary>
internal sealed record SeededRequiredInputs(
    GrimoirePathOptions Options,
    string DataDir,
    string InstructionsDir,
    string SystemPromptPath,
    string DefaultUserPromptPath,
    string PolicyPath,
    string SecretsFilePath,
    string AgentWorkerPath,
    string QueryInstructionsDir,
    string QuerySystemPromptPath,
    string QueryPolicyPath,
    string QueryAgentWorkerPath);

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
    public static GrimoirePathOptions SeedRequiredInputs(string baseDir) =>
        SeedRequiredInputsWithPaths(baseDir).Options;

    /// <summary>Same as <see cref="SeedRequiredInputs"/>, but also returns every seeded file's computed path.</summary>
    public static SeededRequiredInputs SeedRequiredInputsWithPaths(string baseDir)
    {
        var agentWorker = Path.Combine(baseDir, "agent-worker-stub.dll");
        var queryAgentWorker = Path.Combine(baseDir, "query-agent-worker-stub.dll");
        var seeded = SeedRequiredInputFiles(baseDir, agentWorker, queryAgentWorker);
        var options = new GrimoirePathOptions
        {
            BaseDir = baseDir,
            AgentWorker = agentWorker,
            QueryAgentWorker = queryAgentWorker,
        };
        return seeded with { Options = options };
    }

    /// <summary>
    /// Same seeding as <see cref="SeedRequiredInputs"/>, but for zero-configuration tests:
    /// leaves <c>BaseDir</c> unset so the resolver falls back to the process working
    /// directory (FR-003/FR-004). <c>AgentWorker</c> still needs an explicit override —
    /// its own default anchor is the install directory, not the base (research R4), which
    /// a test host can never satisfy.
    /// </summary>
    public static GrimoirePathOptions SeedRequiredInputsForZeroConfig(string cwd)
    {
        var agentWorker = Path.Combine(cwd, "agent-worker-stub.dll");
        var queryAgentWorker = Path.Combine(cwd, "query-agent-worker-stub.dll");
        SeedRequiredInputFiles(cwd, agentWorker, queryAgentWorker);
        return new GrimoirePathOptions { AgentWorker = agentWorker, QueryAgentWorker = queryAgentWorker };
    }

    private static SeededRequiredInputs SeedRequiredInputFiles(string baseDir, string agentWorker, string queryAgentWorker)
    {
        var dataDir = Path.Combine(baseDir, GrimoirePathOptions.DefaultDataDirName);
        var instructionsDir = Path.Combine(dataDir, GrimoirePathOptions.DefaultInstructionsDirRelativePath);
        var secretsFile = Path.Combine(dataDir, GrimoirePathOptions.DefaultSecretsFileName);
        var systemPromptPath = Path.Combine(instructionsDir, "system-prompt.md");
        var defaultUserPromptPath = Path.Combine(instructionsDir, "default-user-prompt.md");
        var policyPath = Path.Combine(instructionsDir, "policy.json");
        var queryInstructionsDir = Path.Combine(dataDir, GrimoirePathOptions.DefaultQueryInstructionsDirRelativePath);
        var querySystemPromptPath = Path.Combine(queryInstructionsDir, "system-prompt.md");
        var queryPolicyPath = Path.Combine(queryInstructionsDir, "policy.json");

        Directory.CreateDirectory(instructionsDir);
        File.WriteAllText(systemPromptPath, "# Test system prompt\nRules.\n");
        File.WriteAllText(defaultUserPromptPath, "Please integrate the source.\n");
        File.WriteAllText(policyPath, ValidPolicyJson);
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(secretsFile, "ANTHROPIC_AUTH_TOKEN=test-token\n");
        File.WriteAllText(agentWorker, "stub");

        Directory.CreateDirectory(queryInstructionsDir);
        File.WriteAllText(querySystemPromptPath, "# Test query system prompt\nRules.\n");
        File.WriteAllText(queryPolicyPath, ValidPolicyJson);
        File.WriteAllText(queryAgentWorker, "stub");

        return new SeededRequiredInputs(
            Options: null!,
            DataDir: dataDir,
            InstructionsDir: instructionsDir,
            SystemPromptPath: systemPromptPath,
            DefaultUserPromptPath: defaultUserPromptPath,
            PolicyPath: policyPath,
            SecretsFilePath: secretsFile,
            AgentWorkerPath: agentWorker,
            QueryInstructionsDir: queryInstructionsDir,
            QuerySystemPromptPath: querySystemPromptPath,
            QueryPolicyPath: queryPolicyPath,
            QueryAgentWorkerPath: queryAgentWorker);
    }
}
