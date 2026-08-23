using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using System.Text.Json;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T058-T059 (026-guarded-tool-surface, US4, ADR-030 R4): behavioral coverage for
/// <c>batch</c>'s dispatch in <see cref="GuardedToolExecutor"/>. Mirrors
/// <c>LintSearchToolTests</c>' idiom: exercises the executor directly with a registry that
/// declares <see cref="ToolRegistry.BatchDefinition"/> — <c>LintToolRegistry.Default</c>
/// does not declare <c>batch</c> yet (deferred to the eval-recapture layer), but the
/// dispatch logic is complete and independently testable.
/// </summary>
public class LintBatchToolTests
{
    private static readonly ToolRegistry BatchCapableRegistry = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.ReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
        ToolRegistry.SearchFilesDefinition,
        ToolRegistry.BatchDefinition,
        ToolRegistry.DeleteFileDefinition,
    ]);

    // ── T058: a batch with one write executes no member at all (SC-007) ────────────────

    [Fact]
    public async Task BatchContainingAWrite_ExecutesNoMemberAtAll_AndIsRejected()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            var pagePath = Path.Combine(wikiRoot, "page.md");
            await File.WriteAllTextAsync(pagePath, "original");

            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writeRules: [new WriteRule(wikiRoot + Path.DirectorySeparatorChar, WriteMode.ReadWrite)]);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: BatchCapableRegistry);

            var batchInput = JsonSerializer.Serialize(new
            {
                calls = new object[]
                {
                    new { tool = "read_file", input = new { path = "page.md" } },
                    new { tool = "write_file", input = new { path = "page.md", content = "smuggled" } },
                },
            });

            var result = await executor.ExecuteAsync(ToolRegistry.Batch, batchInput, turn: 1, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("tool_not_allowed_in_batch", Assert.Single(executor.Denials).Reason);
            Assert.Equal("original", await File.ReadAllTextAsync(pagePath));
            Assert.Empty(executor.TouchedPaths);
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task BatchContainingADelete_IsRejectedWholesale()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: BatchCapableRegistry);

            var batchInput = JsonSerializer.Serialize(new
            {
                calls = new object[] { new { tool = "delete_file", input = new { path = "page.md" } } },
            });

            var result = await executor.ExecuteAsync(ToolRegistry.Batch, batchInput, turn: 1, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("tool_not_allowed_in_batch", Assert.Single(executor.Denials).Reason);
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task NestedBatch_IsRejectedWholesale()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: BatchCapableRegistry);

            var batchInput = JsonSerializer.Serialize(new
            {
                calls = new object[] { new { tool = "batch", input = new { calls = Array.Empty<object>() } } },
            });

            var result = await executor.ExecuteAsync(ToolRegistry.Batch, batchInput, turn: 1, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("nested_batch", Assert.Single(executor.Denials).Reason);
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task BatchOverTwentyCalls_IsRejectedWholesale_AsTooManyCalls()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: BatchCapableRegistry);

            var batchInput = JsonSerializer.Serialize(new
            {
                calls = Enumerable.Range(0, 21)
                    .Select(_ => (object)new { tool = "list_files", input = new { path = "." } })
                    .ToArray(),
            });

            var result = await executor.ExecuteAsync(ToolRegistry.Batch, batchInput, turn: 1, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("too_many_calls", Assert.Single(executor.Denials).Reason);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── T059: a mixed batch returns allowed results plus an individual denial (FR-013) ──

    [Fact]
    public async Task MixedBatch_ReturnsAllowedResults_PlusAnIndividualDenialForTheDeniedMember()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            var techDir = Path.Combine(wikiRoot, "tech");
            var secretDir = Path.Combine(wikiRoot, "secret");
            Directory.CreateDirectory(techDir);
            Directory.CreateDirectory(secretDir);
            await File.WriteAllTextAsync(Path.Combine(techDir, "page.md"), "allowed content");
            await File.WriteAllTextAsync(Path.Combine(secretDir, "hidden.md"), "denied content");

            // Read scope covers only tech/ — secret/ is out of scope entirely.
            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [techDir + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: BatchCapableRegistry);

            var batchInput = JsonSerializer.Serialize(new
            {
                calls = new object[]
                {
                    new { tool = "read_file", input = new { path = "tech/page.md" } },
                    new { tool = "read_file", input = new { path = "secret/hidden.md" } },
                },
            });

            var result = await executor.ExecuteAsync(ToolRegistry.Batch, batchInput, turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("allowed content", result.Content, StringComparison.Ordinal);
            var denial = Assert.Single(executor.Denials);
            Assert.Equal(ToolRegistry.ReadFile, denial.Action);
            Assert.Equal("no_rule", denial.Reason);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── Copilot review (PR #177): a member that fails for a non-policy reason (file not
    // found) is not a "denial" — only an entry actually recorded in Denials counts as one,
    // exactly as SC-002/FR-013 define it for the run as a whole.

    [Fact]
    public async Task BatchMemberThatIsNotFound_IsNotCountedAsADenial()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            await File.WriteAllTextAsync(Path.Combine(wikiRoot, "page.md"), "content");

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: BatchCapableRegistry);

            var batchInput = JsonSerializer.Serialize(new
            {
                calls = new object[]
                {
                    new { tool = "read_file", input = new { path = "page.md" } },
                    new { tool = "read_file", input = new { path = "never-existed.md" } },
                },
            });

            var result = await executor.ExecuteAsync(ToolRegistry.Batch, batchInput, turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("File not found", result.Content, StringComparison.Ordinal);
            Assert.Empty(executor.Denials);
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task BatchOfAllowedCalls_ReturnsEveryMembersResult()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            await File.WriteAllTextAsync(Path.Combine(wikiRoot, "one.md"), "first page");
            await File.WriteAllTextAsync(Path.Combine(wikiRoot, "two.md"), "second page");

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: BatchCapableRegistry);

            var batchInput = JsonSerializer.Serialize(new
            {
                calls = new object[]
                {
                    new { tool = "read_file", input = new { path = "one.md" } },
                    new { tool = "read_file", input = new { path = "two.md" } },
                },
            });

            var result = await executor.ExecuteAsync(ToolRegistry.Batch, batchInput, turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("first page", result.Content, StringComparison.Ordinal);
            Assert.Contains("second page", result.Content, StringComparison.Ordinal);
            Assert.Empty(executor.Denials);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── shared setup ─────────────────────────────────────────────────────────────

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lint-batch-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CleanUp(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
