using Grimoire.Domain.Guardrails;
using Grimoire.AgentRuntime.Guardrails;
using System.Text.Json;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T017 (012-query-synthesis-writes, ADR-015): end-to-end coordination behavior through
/// <see cref="GuardedToolExecutor"/> (not <c>SharedFileWriteGuard</c> in isolation) — the
/// full policy-then-coordination-then-write pipeline wired via the executor's
/// <c>writeLocksDir</c> constructor argument (T016).
/// </summary>
public class GuardedToolExecutorCoordinationTests
{
    [Fact]
    public async Task CreateOnlyPolicy_WriteToExistingTarget_ReturnsIsError_NoFileModified_DenialRecorded()
    {
        var root = CreateTempRoot();
        try
        {
            var pagesDir = Path.Combine(root, "wiki", "pages");
            Directory.CreateDirectory(pagesDir);
            var existingPage = Path.Combine(pagesDir, "existing.md");
            await File.WriteAllTextAsync(existingPage, "original content");

            var executor = BuildExecutor(root, createOnlyPagesPrefix: true);

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "wiki/pages/existing.md", content = "overwrite attempt" }),
                turn: 1,
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Contains("create_only_target_exists", result.Content, StringComparison.Ordinal);
            Assert.Equal("original content", await File.ReadAllTextAsync(existingPage));

            var denial = Assert.Single(executor.Denials);
            Assert.Equal("create_only_target_exists", denial.Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadThenConcurrentlyModifiedThenWrite_ReturnsIsError_WriteConflictStaleRead()
    {
        var root = CreateTempRoot();
        try
        {
            var indexPath = Path.Combine(root, "wiki", "index.md");
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            await File.WriteAllTextAsync(indexPath, "- entry 1");

            var executor = BuildExecutor(root, createOnlyPagesPrefix: false);

            var readResult = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "wiki/index.md" }),
                turn: 1,
                CancellationToken.None);
            Assert.False(readResult.IsError);

            // A concurrent writer (another process/run) changes index.md after this run read it.
            await File.WriteAllTextAsync(indexPath, "- entry 1\n- entry from another writer");

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "wiki/index.md", content = "- entry 1\n- my entry" }),
                turn: 2,
                CancellationToken.None);

            Assert.True(writeResult.IsError);
            Assert.Contains("write_conflict_stale_read", writeResult.Content, StringComparison.Ordinal);
            Assert.Equal("- entry 1\n- entry from another writer", await File.ReadAllTextAsync(indexPath));

            var denial = Assert.Single(executor.Denials);
            Assert.Equal("write_conflict_stale_read", denial.Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadThenWriteUnmodified_Succeeds_ExactlyAsToday_AndUpdatesTouchedPaths()
    {
        var root = CreateTempRoot();
        try
        {
            var indexPath = Path.Combine(root, "wiki", "index.md");
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            await File.WriteAllTextAsync(indexPath, "- entry 1");

            var executor = BuildExecutor(root, createOnlyPagesPrefix: false);

            var readResult = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "wiki/index.md" }),
                turn: 1,
                CancellationToken.None);
            Assert.False(readResult.IsError);

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "wiki/index.md", content = "- entry 1\n- entry 2" }),
                turn: 2,
                CancellationToken.None);

            Assert.False(writeResult.IsError);
            Assert.Equal("- entry 1\n- entry 2", await File.ReadAllTextAsync(indexPath));
            Assert.Empty(executor.Denials);
            Assert.Contains(indexPath, executor.TouchedPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PolicyScopeDenial_NeverReachesCoordinationGuard_NoLockFileCreatedForDeniedTarget()
    {
        var root = CreateTempRoot();
        try
        {
            var writeLocksDir = Path.Combine(root, "write-locks");
            var executor = BuildExecutor(root, createOnlyPagesPrefix: false, writeLocksDir: writeLocksDir);

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "outside-scope/forbidden.md", content = "should never land" }),
                turn: 1,
                CancellationToken.None);

            Assert.True(result.IsError);
            var denial = Assert.Single(executor.Denials);
            Assert.Equal("out_of_scope", denial.Reason);

            // No coordination lock file was ever created — the guard was never reached.
            if (Directory.Exists(writeLocksDir))
            {
                Assert.Empty(Directory.GetFiles(writeLocksDir));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NoWriteLocksDirSupplied_BehavesExactlyAsBeforeThisFeature_NoCoordinationEnforced()
    {
        // Backward compatibility check for the optional constructor parameter (T016):
        // an executor built the "old way" (no writeLocksDir) must allow a write to an
        // existing target it never read — today's exact behavior, unaffected by ADR-015.
        var root = CreateTempRoot();
        try
        {
            var pagesDir = Path.Combine(root, "wiki", "pages");
            Directory.CreateDirectory(pagesDir);
            var existingPage = Path.Combine(pagesDir, "existing.md");
            await File.WriteAllTextAsync(existingPage, "original content");

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [Path.Combine(root, "wiki", "pages") + Path.DirectorySeparatorChar]);
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root); // no writeLocksDir

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "wiki/pages/existing.md", content = "overwritten, never read first" }),
                turn: 1,
                CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Equal("overwritten, never read first", await File.ReadAllTextAsync(existingPage));
            Assert.Empty(executor.Denials);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static GuardedToolExecutor BuildExecutor(string root, bool createOnlyPagesPrefix, string? writeLocksDir = null)
    {
        var pagesPrefix = Path.Combine(root, "wiki", "pages") + Path.DirectorySeparatorChar;
        var indexPath = Path.Combine(root, "wiki", "index.md");

        var policy = new SafetyPolicy(
            root,
            readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
            writeRules:
            [
                new WriteRule(pagesPrefix, CreateOnly: createOnlyPagesPrefix),
                new WriteRule(indexPath, CreateOnly: false),
            ]);

        var journal = new WriteJournal();
        return new GuardedToolExecutor(
            policy,
            journal,
            root,
            writeLocksDir: writeLocksDir ?? Path.Combine(root, "write-locks"));
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"guarded-executor-coordination-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
