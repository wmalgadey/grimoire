using Grimoire.IngestAgent.Guardrails;

namespace Grimoire.IntegrationTests;

public class GuardrailPolicyTests
{
    [Fact]
    public async Task PolicyLoader_Loads_And_Enforces_DenyByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), $"guardrail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var policyPath = Path.Combine(root, "ingest-guardrails.yml");
        await File.WriteAllTextAsync(policyPath,
            "version: \"1\"\n" +
            "deny_by_default: true\n" +
            "write_allow_prefixes:\n" +
            "  - wiki/\n" +
            "read_allow_paths:\n" +
            "  - specs/\n");

        var policy = await new GuardrailPolicyLoader().LoadAsync(policyPath, CancellationToken.None);
        var evaluator = new GuardrailEvaluator(policy);

        Assert.True(evaluator.Evaluate(GuardrailAction.Write, "wiki/pages/a.md").IsAllowed);
        Assert.False(evaluator.Evaluate(GuardrailAction.Write, "backend/src/a.cs").IsAllowed);
    }
}
