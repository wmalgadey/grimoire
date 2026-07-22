namespace Grimoire.AgentEvals;

/// <summary>
/// T024-T025 (US3) — FR-009/SC-002 (every eval run record names the model that produced
/// it) and FR-011 (the Anthropic path is untouched by this feature). Points both provider
/// paths at an unreachable local port so runs fail fast and hermetically — the fact under
/// test is that <c>TaskArtifactDocument.Model</c> is populated from the resolved
/// configuration regardless of run outcome (AgentEvalSupport.cs already does this for both
/// the completed and failed code paths). Mutates real process environment variables, so
/// this runs in the sequential <c>EvalProviderEnvironment</c> collection.
/// </summary>
[Collection("EvalProviderEnvironment")]
public class EvalProviderTransparencyTests
{
    [Fact]
    public async Task RunAsync_AffordableConfigured_ArtifactModelMatchesConfiguredModel()
    {
        var saved = SaveEnv();
        try
        {
            ClearProviderEnv();
            Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_BASE_URL", "http://localhost:1");
            Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_MODEL", "nvidia-model");
            Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY", "fake-affordable-key");

            var runner = new AgentEvalRunner();
            var result = await runner.RunAsync(
                fixtureName: "empty-topic",
                sourceContent: "Transparency probe (affordable).",
                runLabel: "transparency-affordable",
                mutateSystemPrompt: null,
                cancellationToken: CancellationToken.None);

            Assert.Equal("nvidia-model", result.Artifact.Model);
        }
        finally
        {
            RestoreEnv(saved);
        }
    }

    [Fact]
    public async Task RunAsync_AnthropicOnlyConfigured_ArtifactModelMatches_AndLeavesIngestEnvUnchanged()
    {
        var saved = SaveEnv();
        try
        {
            ClearProviderEnv();
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "sk-ant-fake-transparency-token");
            Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_BASE_URL", "http://localhost:1");
            Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_MODEL", "claude-transparency-test-model");

            var runner = new AgentEvalRunner();
            var result = await runner.RunAsync(
                fixtureName: "empty-topic",
                sourceContent: "Transparency probe (anthropic).",
                runLabel: "transparency-anthropic",
                mutateSystemPrompt: null,
                cancellationToken: CancellationToken.None);

            Assert.Equal("claude-transparency-test-model", result.Artifact.Model);

            // FR-011: RunAsync must not rewrite GRIMOIRE_INGEST_BASE_URL/MODEL when
            // Kind == Anthropic — that rewrite is scoped to the affordable path only.
            Assert.Equal("http://localhost:1", Environment.GetEnvironmentVariable("GRIMOIRE_INGEST_BASE_URL"));
            Assert.Equal("claude-transparency-test-model", Environment.GetEnvironmentVariable("GRIMOIRE_INGEST_MODEL"));
        }
        finally
        {
            RestoreEnv(saved);
        }
    }

    [Fact]
    public async Task RunAsync_BothProvidersRunSeparately_EachArtifactNamesItsOwnModel()
    {
        var saved = SaveEnv();
        try
        {
            ClearProviderEnv();
            Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_BASE_URL", "http://localhost:1");
            Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_MODEL", "nvidia-model");
            Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY", "fake-affordable-key");
            var affordableResult = await new AgentEvalRunner().RunAsync(
                fixtureName: "empty-topic",
                sourceContent: "Both-providers probe (affordable).",
                runLabel: "both-affordable",
                mutateSystemPrompt: null,
                cancellationToken: CancellationToken.None);

            ClearProviderEnv();
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "sk-ant-fake-transparency-token");
            Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_BASE_URL", "http://localhost:1");
            Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_MODEL", "claude-transparency-test-model");
            var anthropicResult = await new AgentEvalRunner().RunAsync(
                fixtureName: "empty-topic",
                sourceContent: "Both-providers probe (anthropic).",
                runLabel: "both-anthropic",
                mutateSystemPrompt: null,
                cancellationToken: CancellationToken.None);

            Assert.Equal("nvidia-model", affordableResult.Artifact.Model);
            Assert.Equal("claude-transparency-test-model", anthropicResult.Artifact.Model);
            Assert.NotEqual(affordableResult.Artifact.Model, anthropicResult.Artifact.Model);
        }
        finally
        {
            RestoreEnv(saved);
        }
    }

    private static readonly string[] ProviderEnvKeys =
    [
        "ANTHROPIC_AUTH_TOKEN",
        "GRIMOIRE_EVAL_PROVIDER_BASE_URL",
        "GRIMOIRE_EVAL_PROVIDER_MODEL",
        "GRIMOIRE_EVAL_PROVIDER_API_KEY",
        "GRIMOIRE_INGEST_BASE_URL",
        "GRIMOIRE_INGEST_MODEL",
    ];

    private static Dictionary<string, string?> SaveEnv()
        => ProviderEnvKeys.ToDictionary(k => k, Environment.GetEnvironmentVariable, StringComparer.Ordinal);

    private static void ClearProviderEnv()
    {
        foreach (var key in ProviderEnvKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    private static void RestoreEnv(Dictionary<string, string?> saved)
    {
        foreach (var (key, value) in saved)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
