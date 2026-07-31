using Grimoire.AgentRuntime.Instructions;
using Grimoire.Domain.Guardrails;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T008 (013-lint-agent, ADR-016): loading a policy with a write-rule
/// <c>"mode": "frontmatter-only"</c> produces a policy whose write-scope
/// <see cref="SafetyPolicy.Evaluate"/> returns <see cref="WriteMode.FrontmatterOnly"/>.
/// The existing <see cref="PolicyLoaderModeTests"/> suite (read-write/create-only) is left
/// entirely unmodified — this is a purely additive third mode value.
/// </summary>
public class PolicyLoaderFrontmatterOnlyModeTests
{
    [Fact]
    public async Task FrontmatterOnlyMode_ProducesPolicy_WhoseWriteScopeEvaluate_ReturnsModeFrontmatterOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-mode-frontmatter-only-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 1,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "concepts/"}],
                  "write": [{"pathPrefix": "concepts/", "mode": "frontmatter-only"}]
                }
                """);

            var loader = new PolicyLoader(root);
            var result = await loader.LoadAsync(policyPath, CancellationToken.None);

            Assert.True(result.IsFirst(out var loaded));
            var decision = loaded.Policy.Evaluate(Path.Combine(root, "concepts", "existing.md"), isWrite: true);

            Assert.True(decision.IsAllowed);
            Assert.Equal(WriteMode.FrontmatterOnly, decision.Mode);
            Assert.False(decision.IsCreateOnly);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FrontmatterOnlyMode_ReadRuleIsUnaffected_OnlyWriteScopeCarriesMode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-mode-frontmatter-only-read-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 1,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "concepts/"}, {"pathPrefix": "index.md"}],
                  "write": [{"pathPrefix": "concepts/", "mode": "frontmatter-only"}]
                }
                """);

            var loader = new PolicyLoader(root);
            var result = await loader.LoadAsync(policyPath, CancellationToken.None);

            Assert.True(result.IsFirst(out var loaded));

            // No write rule for index.md (013-lint-agent data-model.md: "no write rule for
            // index.md/log.md — Lint does not maintain the index or log").
            var readDecision = loaded.Policy.Evaluate(Path.Combine(root, "index.md"), isWrite: false);
            Assert.True(readDecision.IsAllowed);

            var deniedWriteDecision = loaded.Policy.Evaluate(Path.Combine(root, "index.md"), isWrite: true);
            Assert.False(deniedWriteDecision.IsAllowed);
            Assert.Equal("out_of_scope", deniedWriteDecision.DenialReason);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
