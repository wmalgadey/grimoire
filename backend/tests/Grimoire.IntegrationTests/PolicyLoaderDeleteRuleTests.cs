using Grimoire.AgentRuntime.Instructions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T007/T008 (026-guarded-tool-surface, ADR-031 R3): a real policy file's <c>delete</c>
/// section, parsed through <see cref="PolicyLoader"/> end to end (JSON → schema →
/// <c>SafetyPolicy</c>). <see cref="Grimoire.Domain.UnitTests.DeleteRuleTests"/> covers
/// <c>SafetyPolicy.EvaluateDelete</c>'s own logic directly, bypassing JSON deserialization;
/// <see cref="AgentDeleteScopeNotInheritedTests"/> exercises only policies with no
/// <c>delete</c> section at all. Neither would catch a regression in
/// <c>PolicyLoader.PolicyFileSchema.Delete</c> or its prefix normalization — this is the
/// positive real-file case: a policy that *does* declare a delete rule, loaded the way
/// production code actually loads it. Mirrors <see cref="PolicyLoaderFrontmatterOnlyModeTests"/>'s
/// "real policy file" idiom.
/// </summary>
public class PolicyLoaderDeleteRuleTests
{
    [Fact]
    public async Task DeleteRule_ProducesPolicy_WhoseEvaluateDelete_AllowsInScopeAndDeniesOutOfScope()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-delete-rule-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 2,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "."}],
                  "write": [{"pathPrefix": "."}],
                  "delete": [{"pathPrefix": "."}]
                }
                """);

            var loader = new PolicyLoader(root);
            var result = await loader.LoadAsync(policyPath, CancellationToken.None);

            Assert.True(result.IsFirst(out var loaded));

            var inScope = loaded.Policy.EvaluateDelete(Path.Combine(root, "tech", "page.md"));
            Assert.True(inScope.IsAllowed);

            var outOfScope = loaded.Policy.EvaluateDelete(Path.Combine(Path.GetDirectoryName(root)!, "outside.md"));
            Assert.False(outOfScope.IsAllowed);
            Assert.Equal("traversal", outOfScope.DenialReason);
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
    public async Task DeleteRule_WithExcludePrefix_DeniesTheExcludedPathEvenInsideAMatchingPrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-delete-rule-exclude-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 2,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "."}],
                  "write": [{"pathPrefix": "."}],
                  "delete": [{"pathPrefix": ".", "excludePrefixes": ["index.md"]}]
                }
                """);

            var loader = new PolicyLoader(root);
            var result = await loader.LoadAsync(policyPath, CancellationToken.None);

            Assert.True(result.IsFirst(out var loaded));

            var excluded = loaded.Policy.EvaluateDelete(Path.Combine(root, "index.md"));
            Assert.False(excluded.IsAllowed);
            Assert.Equal("no_rule", excluded.DenialReason);

            var included = loaded.Policy.EvaluateDelete(Path.Combine(root, "tech", "page.md"));
            Assert.True(included.IsAllowed);
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
    public async Task NoDeleteSection_ProducesPolicy_WhoseEvaluateDelete_AlwaysDenies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-no-delete-section-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 2,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "."}],
                  "write": [{"pathPrefix": "."}]
                }
                """);

            var loader = new PolicyLoader(root);
            var result = await loader.LoadAsync(policyPath, CancellationToken.None);

            Assert.True(result.IsFirst(out var loaded));

            var decision = loaded.Policy.EvaluateDelete(Path.Combine(root, "tech", "page.md"));
            Assert.False(decision.IsAllowed);
            Assert.Equal("no_rule", decision.DenialReason);
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
