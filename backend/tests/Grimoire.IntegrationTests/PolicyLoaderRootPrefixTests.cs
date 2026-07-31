using Grimoire.AgentRuntime.Instructions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T009 (014-wiki-storage-restructure, Foundational, R3) — the <c>"."</c> root-directory
/// prefix: <c>PolicyLoader.NormalizeRulePrefix</c> resolves the literal <c>"."</c> to the
/// wiki root itself, treated directory-style (matches the root and everything beneath it,
/// including nested category files under the flattened layout) — and confirms
/// first-match-wins ordering still lets an exact-match rule placed before <c>"."</c> win,
/// even though <c>"."</c> would also match that same target.
/// </summary>
public class PolicyLoaderRootPrefixTests
{
    [Fact]
    public async Task DotPrefix_MatchesWikiRootAndEveryNestedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-root-prefix-dot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 1,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "."}],
                  "write": [{"pathPrefix": "."}]
                }
                """);

            var loader = new PolicyLoader(root);
            var result = await loader.LoadAsync(policyPath, CancellationToken.None);

            Assert.True(result.IsFirst(out var loaded));

            // The wiki root itself.
            var rootDecision = loaded.Policy.Evaluate(root, isWrite: false);
            Assert.True(rootDecision.IsAllowed);

            // A top-level file directly under the root.
            var topLevelDecision = loaded.Policy.Evaluate(Path.Combine(root, "index.md"), isWrite: true);
            Assert.True(topLevelDecision.IsAllowed);

            // A nested category file — the flattened layout's normal case
            // (014-wiki-storage-restructure removes the "pages/" wrapper).
            var nestedDecision = loaded.Policy.Evaluate(Path.Combine(root, "concepts", "foo.md"), isWrite: true);
            Assert.True(nestedDecision.IsAllowed);
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
    public async Task ExactMatchRule_BeforeDotInRuleList_StillWins_FirstMatchWins()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-root-prefix-ordering-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            // index.md (exact-match, create-only) precedes "." (plain read-write) — first-
            // match-wins must let index.md's own rule/mode win, even though "." would also
            // match index.md if it were evaluated first.
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 2,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "."}],
                  "write": [
                    {"pathPrefix": "index.md", "mode": "create-only"},
                    {"pathPrefix": "."}
                  ]
                }
                """);

            var loader = new PolicyLoader(root);
            var result = await loader.LoadAsync(policyPath, CancellationToken.None);

            Assert.True(result.IsFirst(out var loaded));

            var indexDecision = loaded.Policy.Evaluate(Path.Combine(root, "index.md"), isWrite: true);
            Assert.True(indexDecision.IsAllowed);
            Assert.True(indexDecision.IsCreateOnly);

            // A different path only "." covers — still plain read-write, proving "." itself
            // was loaded correctly and is not accidentally shadowed or skipped.
            var otherDecision = loaded.Policy.Evaluate(Path.Combine(root, "concepts", "foo.md"), isWrite: true);
            Assert.True(otherDecision.IsAllowed);
            Assert.False(otherDecision.IsCreateOnly);
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
