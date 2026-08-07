using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>Computed locations of the files <see cref="PathConfigurationTestHelpers"/> seeds, for tests that corrupt one at a time.</summary>
internal sealed record SeededRequiredInputs(
    GrimoirePathOptions Options,
    string DataDir,
    string WikiDir,
    string AgentDir,
    string IngestDir,
    string SystemPromptPath,
    string DefaultUserPromptPath,
    string PolicyPath,
    string SecretsFilePath,
    string AgentWorkerPath,
    string QueryDir,
    string QuerySystemPromptPath,
    string QueryPolicyPath,
    string QueryAgentWorkerPath,
    string LintDir,
    string LintSystemPromptPath,
    string LintPolicyPath,
    string LintAgentWorkerPath);

/// <summary>
/// Shared fixture setup for the path-configuration hermetic test suite (ADR-022): seeds
/// the required-input files a <see cref="GrimoirePathResolver"/> validation pass needs to
/// succeed — an agent directory with all three agent-type subfolders (worker stub DLL +
/// Instructions/) and a secrets file — under a documented layout.
/// </summary>
internal static class PathConfigurationTestHelpers
{
    public const string ValidPolicyJson =
        """
        {
          "version": 1,
          "defaultDecision": "deny",
          "read": [{"pathPrefix": "."}],
          "write": [{"pathPrefix": "."}]
        }
        """;

    /// <summary>
    /// Seeds the secrets file and a complete agent directory (all three agent types) under
    /// <paramref name="root"/>, and returns options with <c>DataDir</c>, <c>WikiDir</c> and
    /// <c>AgentDir</c> all set to sibling locations under <paramref name="root"/> — the
    /// three roots are mandatory (FR-005); there is no code-level fallback to omit them.
    /// </summary>
    public static GrimoirePathOptions SeedRequiredInputs(string root) =>
        SeedRequiredInputsWithPaths(root).Options;

    /// <summary>Same as <see cref="SeedRequiredInputs"/>, but also returns every seeded file's computed path.</summary>
    public static SeededRequiredInputs SeedRequiredInputsWithPaths(string root)
    {
        var dataDir = Path.Combine(root, "data-dir");
        var wikiDir = Path.Combine(root, "wiki-dir");
        var agentDir = Path.Combine(root, "agent-dir");
        // SecretsFile anchors at the process working directory, never at any of the three
        // roots (FR-019) — an explicit absolute value here keeps this helper hermetic and
        // independent of the real test-runner cwd, matching how DataDir/WikiDir/AgentDir
        // are already explicit absolute overrides rather than relying on ambient defaults.
        var secretsFile = Path.Combine(root, ".env");
        var seeded = SeedRequiredInputFiles(dataDir, wikiDir, agentDir, secretsFile);
        // Sub-path keys mirror contracts/appsettings-paths.md's shipped relative values —
        // ADR-022 rule R2 means the resolver has no code-level default to fall back to, so
        // this helper (simulating the shipped appsettings.json) must supply every key
        // itself, not just the three roots.
        var options = new GrimoirePathOptions
        {
            DataDir = dataDir,
            WikiDir = wikiDir,
            AgentDir = agentDir,
            RawDir = "raw",
            StateDb = "state/operational-state.db",
            WriteLocksDir = "write-locks",
            TasksDir = "tasks",
            ConversationsDir = "conversations",
            FindingsDir = "findings",
            RemediationTasksDir = "remediation-tasks",
            SecretsFile = secretsFile,
        };
        return seeded with { Options = options };
    }

    /// <summary>
    /// Same seeding as <see cref="SeedRequiredInputs"/>, but for "zero flags" tests: seeds
    /// the agent directory and secrets file at the documented DEFAULT relative locations
    /// (<c>.grimoire/</c>, <c>llm-wiki/</c>, <c>.grimoire/agents/</c>, <c>.env</c>) beneath
    /// <paramref name="cwd"/>, and returns options with <c>DataDir</c>/<c>WikiDir</c>/
    /// <c>AgentDir</c> set to those same relative default values — simulating exactly what
    /// the shipped <c>appsettings.json</c> supplies (ADR-022: there is no code-level
    /// fallback any more; "zero flags" means the config-file tier is what supplies the
    /// three roots, not an unset <see cref="GrimoirePathOptions"/>). Callers must set the
    /// real process working directory to <paramref name="cwd"/> first ([Collection
    /// ("CurrentDirectoryMutation")]) — SecretsFile always anchors there, never at a root.
    /// </summary>
    public static GrimoirePathOptions SeedRequiredInputsForZeroConfig(string cwd)
    {
        var dataDir = Path.Combine(cwd, ".grimoire");
        var wikiDir = Path.Combine(cwd, "llm-wiki");
        var agentDir = Path.Combine(dataDir, "agents");
        var secretsFile = Path.Combine(cwd, ".env");
        SeedRequiredInputFiles(dataDir, wikiDir, agentDir, secretsFile);

        // Mirrors contracts/appsettings-paths.md's shipped content exactly — ADR-022 rule
        // R2 means the resolver has NO code-level default for any of these (including the
        // sub-paths), so a helper simulating "zero CLI/env flags" must supply every key
        // itself, exactly as the real appsettings.json does, rather than leaving fields
        // null and relying on a fallback that no longer exists.
        return new GrimoirePathOptions
        {
            DataDir = ".grimoire",
            WikiDir = "llm-wiki",
            AgentDir = "agents",
            RawDir = "raw",
            StateDb = "state/operational-state.db",
            WriteLocksDir = "write-locks",
            TasksDir = "tasks",
            ConversationsDir = "conversations",
            FindingsDir = "findings",
            RemediationTasksDir = "remediation-tasks",
            SecretsFile = ".env",
        };
    }

    private static SeededRequiredInputs SeedRequiredInputFiles(string dataDir, string wikiDir, string agentDir, string secretsFile)
    {
        var (ingestDir, systemPromptPath, defaultUserPromptPath, policyPath, agentWorker) =
            SeedAgentType(agentDir, "ingest", GrimoirePathOptions.DefaultAgentWorkerFileName, includeDefaultUserPrompt: true);
        var (queryDir, querySystemPromptPath, _, queryPolicyPath, queryAgentWorker) =
            SeedAgentType(agentDir, "query", GrimoirePathOptions.DefaultQueryAgentWorkerFileName, includeDefaultUserPrompt: false);
        var (lintDir, lintSystemPromptPath, _, lintPolicyPath, lintAgentWorker) =
            SeedAgentType(agentDir, "lint", GrimoirePathOptions.DefaultLintAgentWorkerFileName, includeDefaultUserPrompt: false);

        var secretsFileDir = Path.GetDirectoryName(secretsFile);
        if (!string.IsNullOrEmpty(secretsFileDir))
        {
            Directory.CreateDirectory(secretsFileDir);
        }
        File.WriteAllText(secretsFile, "ANTHROPIC_AUTH_TOKEN=test-token\n");

        return new SeededRequiredInputs(
            Options: null!,
            DataDir: dataDir,
            WikiDir: wikiDir,
            AgentDir: agentDir,
            IngestDir: ingestDir,
            SystemPromptPath: systemPromptPath,
            DefaultUserPromptPath: defaultUserPromptPath,
            PolicyPath: policyPath,
            SecretsFilePath: secretsFile,
            AgentWorkerPath: agentWorker,
            QueryDir: queryDir,
            QuerySystemPromptPath: querySystemPromptPath,
            QueryPolicyPath: queryPolicyPath,
            QueryAgentWorkerPath: queryAgentWorker,
            LintDir: lintDir,
            LintSystemPromptPath: lintSystemPromptPath,
            LintPolicyPath: lintPolicyPath,
            LintAgentWorkerPath: lintAgentWorker);
    }

    /// <summary>
    /// Seeds a complete, independently valid agent runtime (all three agent types) at an
    /// arbitrary directory — for tests that relocate <c>AgentDir</c> itself (e.g. via
    /// precedence or a custom <c>--agent-dir</c>) and need the destination to already
    /// satisfy every <see cref="GrimoirePathResolver"/> agent-runtime check.
    /// </summary>
    public static void SeedAgentRuntimeAt(string agentDir)
    {
        SeedAgentType(agentDir, "ingest", GrimoirePathOptions.DefaultAgentWorkerFileName, includeDefaultUserPrompt: true);
        SeedAgentType(agentDir, "query", GrimoirePathOptions.DefaultQueryAgentWorkerFileName, includeDefaultUserPrompt: false);
        SeedAgentType(agentDir, "lint", GrimoirePathOptions.DefaultLintAgentWorkerFileName, includeDefaultUserPrompt: false);
    }

    /// <summary>
    /// Seeds one agent type's complete subfolder under the agent directory: the worker
    /// stub DLL at its root, and its instruction documents under <c>Instructions/</c>
    /// (data-model.md §2/§6 build contract — the hub reads this shape, the real agent
    /// build produces it).
    /// </summary>
    private static (string Dir, string SystemPromptPath, string DefaultUserPromptPath, string PolicyPath, string WorkerPath) SeedAgentType(
        string agentDir, string agentId, string workerFileName, bool includeDefaultUserPrompt)
    {
        var dir = Path.Combine(agentDir, agentId);
        var instructionsDir = Path.Combine(dir, "Instructions");
        var systemPromptPath = Path.Combine(instructionsDir, "system-prompt.md");
        var defaultUserPromptPath = Path.Combine(instructionsDir, "default-user-prompt.md");
        var policyPath = Path.Combine(instructionsDir, "policy.json");
        var workerPath = Path.Combine(dir, workerFileName);

        Directory.CreateDirectory(instructionsDir);
        File.WriteAllText(systemPromptPath, $"# Test {agentId} system prompt\nRules.\n");
        if (includeDefaultUserPrompt)
        {
            File.WriteAllText(defaultUserPromptPath, "Please integrate the source.\n");
        }
        File.WriteAllText(policyPath, ValidPolicyJson);
        File.WriteAllText(workerPath, "stub");

        return (dir, systemPromptPath, defaultUserPromptPath, policyPath, workerPath);
    }
}
