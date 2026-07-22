namespace Grimoire.AgentEvals;

/// <summary>
/// T014 (US1) — FR-004: an unreachable affordable-provider endpoint must fail the eval
/// sample with an actionable connectivity error, not skip and not get misreported as an
/// agent-judgment failure. Mutates real process environment variables (RunAsync reads them
/// directly), so this runs in the sequential <c>EvalProviderEnvironment</c> collection.
/// </summary>
[Collection("EvalProviderEnvironment")]
public class EvalConnectivityTests
{
    [Fact]
    public async Task RunAsync_AffordableEndpointUnreachable_FailsWithConnectivityError()
    {
        var saved = SaveProviderEnv();
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", null);
            // Nothing listens on port 1 (a reserved, unprivileged-inaccessible port) —
            // the connection is refused immediately rather than timing out.
            Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_BASE_URL", "http://localhost:1");
            Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_MODEL", "nvidia-model");
            Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY", "fake-affordable-key");

            var runner = new AgentEvalRunner();
            var result = await runner.RunAsync(
                fixtureName: "empty-topic",
                sourceContent: "Connectivity probe content for FR-004.",
                runLabel: "connectivity-probe",
                mutateSystemPrompt: null,
                cancellationToken: CancellationToken.None);

            Assert.Equal("failed", result.Status);
            Assert.NotNull(result.Artifact.FailureReason);
            Assert.Contains("refused", result.Artifact.FailureReason, StringComparison.OrdinalIgnoreCase);

            // Distinct from an agent-judgment failure: no wiki content was ever touched.
            Assert.Empty(result.PageFiles);
        }
        finally
        {
            RestoreProviderEnv(saved);
        }
    }

    private static Dictionary<string, string?> SaveProviderEnv()
    {
        string[] keys =
        [
            "ANTHROPIC_AUTH_TOKEN",
            "GRIMOIRE_EVAL_PROVIDER_BASE_URL",
            "GRIMOIRE_EVAL_PROVIDER_MODEL",
            "GRIMOIRE_EVAL_PROVIDER_API_KEY",
            "GRIMOIRE_INGEST_BASE_URL",
            "GRIMOIRE_INGEST_MODEL",
        ];

        return keys.ToDictionary(k => k, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
    }

    private static void RestoreProviderEnv(Dictionary<string, string?> saved)
    {
        foreach (var (key, value) in saved)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
