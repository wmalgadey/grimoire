using Grimoire.AgentRuntime.Instructions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T011 (012-query-synthesis-writes, ADR-015): loading a policy with a write-rule
/// <c>mode</c> field. Covers contract §1: <c>mode</c> optional/additive, recognized
/// values <c>"read-write"</c>/<c>"create-only"</c>, fail-closed on anything else, and
/// byte-for-byte backward compatibility with existing mode-less policy files
/// (e.g. <c>data/agents/ingest/policy.json</c>).
/// </summary>
public class PolicyLoaderModeTests
{
    [Fact]
    public async Task CreateOnlyMode_ProducesPolicy_WhoseWriteScopeEvaluate_ReturnsIsCreateOnlyTrue()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-mode-create-only-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 2,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "."}],
                  "write": [{"pathPrefix": ".", "mode": "create-only"}]
                }
                """);

            var loader = new PolicyLoader(root);
            var result = await loader.LoadAsync(policyPath, CancellationToken.None);

            Assert.True(result.IsFirst(out var loaded));
            var decision = loaded.Policy.Evaluate(Path.Combine(root, "concepts", "new.md"), isWrite: true);

            Assert.True(decision.IsAllowed);
            Assert.True(decision.IsCreateOnly);
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
    public async Task ModeAbsent_DefaultsToReadWrite_IsCreateOnlyFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-mode-absent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 1,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "."}],
                  "write": [{"pathPrefix": "index.md"}]
                }
                """);

            var loader = new PolicyLoader(root);
            var result = await loader.LoadAsync(policyPath, CancellationToken.None);

            Assert.True(result.IsFirst(out var loaded));
            var decision = loaded.Policy.Evaluate(Path.Combine(root, "index.md"), isWrite: true);

            Assert.True(decision.IsAllowed);
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
    public async Task ExplicitReadWriteMode_BehavesIdenticallyToModeAbsent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-mode-explicit-rw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 2,
                  "defaultDecision": "deny",
                  "read": [],
                  "write": [{"pathPrefix": "log.md", "mode": "read-write"}]
                }
                """);

            var loader = new PolicyLoader(root);
            var result = await loader.LoadAsync(policyPath, CancellationToken.None);

            Assert.True(result.IsFirst(out var loaded));
            var decision = loaded.Policy.Evaluate(Path.Combine(root, "log.md"), isWrite: true);

            Assert.True(decision.IsAllowed);
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
    public async Task BogusMode_FailsClosed_WithClearReason()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-mode-bogus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 2,
                  "defaultDecision": "deny",
                  "read": [],
                  "write": [{"pathPrefix": ".", "mode": "bogus"}]
                }
                """);

            var loader = new PolicyLoader(root);
            var result = await loader.LoadAsync(policyPath, CancellationToken.None);

            Assert.True(result.IsSecond(out var failure));
            Assert.Contains("bogus", failure.Reason, StringComparison.Ordinal);
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
    public async Task ExistingIngestPolicyFile_WithNoModeFieldAnywhere_LoadsByteForByteIdenticallyToToday()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-mode-ingest-unaffected-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var repoRoot = FindRepositoryRoot();
            var ingestPolicyPath = Path.Combine(repoRoot, "data", "agents", "ingest", "policy.json");
            Assert.True(File.Exists(ingestPolicyPath), $"Expected repo file not found: {ingestPolicyPath}");

            var loader = new PolicyLoader(root);
            var result = await loader.LoadAsync(ingestPolicyPath, CancellationToken.None);

            Assert.True(result.IsFirst(out var loaded));

            // Every write rule in the real Ingest policy is mode-absent — every allowed
            // write-scope decision it produces must be plain read-write.
            var conceptsDecision = loaded.Policy.Evaluate(Path.Combine(root, "concepts", "anything.md"), isWrite: true);
            var indexDecision = loaded.Policy.Evaluate(Path.Combine(root, "index.md"), isWrite: true);
            var logDecision = loaded.Policy.Evaluate(Path.Combine(root, "log.md"), isWrite: true);

            foreach (var decision in new[] { conceptsDecision, indexDecision, logDecision })
            {
                if (decision.IsAllowed)
                {
                    Assert.False(decision.IsCreateOnly);
                }
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "data", "agents", "ingest")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root from " + AppContext.BaseDirectory);
    }
}
