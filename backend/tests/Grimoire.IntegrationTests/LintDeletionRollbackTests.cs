using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using System.Text.Json;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T037 (026-guarded-tool-surface, US2, ADR-031 R4, SC-005a): a page deleted by a run that
/// then fails is restored by the journal. Exercises <see cref="GuardedToolExecutor"/> and
/// <see cref="WriteJournal"/> directly, mirroring
/// <c>IngestRunLifecycleTests.MidRunFailureAfterTwoWrites_RollbackRestoresUpdatedAndCreatedPaths</c>'s
/// idiom — <see cref="WriteJournal.RollbackAsync"/> is the one rollback mechanism every
/// guarded agent shares (ADR-006); this proves it restores a <em>deleted</em> file exactly
/// as it restores an overwritten one, which is what ADR-031 R4 adds.
/// </summary>
public class LintDeletionRollbackTests
{
    private static readonly ToolRegistry FullScopeRegistry = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.ReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
        ToolRegistry.DeleteFileDefinition,
    ]);

    [Fact]
    public async Task PageDeletedThisRun_RestoredByJournalRollback_WithItsOriginalContentByteForByte()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(Path.Combine(wikiRoot, "tech"));
            var pagePath = Path.Combine(wikiRoot, "tech", "obsolete.md");
            const string originalContent = "---\ntitle: Obsolete Page\n---\n\nThis page will be deleted.\n";
            await File.WriteAllTextAsync(pagePath, originalContent);

            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writeRules: [new WriteRule(wikiRoot + Path.DirectorySeparatorChar, WriteMode.ReadWrite)],
                deleteRules: [new DeleteRule(wikiRoot + Path.DirectorySeparatorChar)]);
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, wikiRoot, registry: FullScopeRegistry);

            var deleteResult = await executor.ExecuteAsync(
                ToolRegistry.DeleteFile,
                JsonSerializer.Serialize(new { path = "tech/obsolete.md" }),
                turn: 1, CancellationToken.None);

            Assert.False(deleteResult.IsError);
            Assert.False(File.Exists(pagePath));
            var deleted = Assert.Single(executor.DeletedPaths);
            Assert.Equal(1, deleted.Turn);

            // The run then fails (simulated here directly — production wiring is
            // LintIntentHandler/RemediationExecutionIntentHandler.DescribeUnhandledFailureAsync,
            // invoked by AgentHost's catch on any unhandled exception or AgentLoopCapException).
            var rollback = await journal.RollbackAsync(CancellationToken.None);

            Assert.True(rollback[deleted.Path]);
            Assert.True(File.Exists(pagePath));
            Assert.Equal(originalContent, await File.ReadAllTextAsync(pagePath));
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task MultiplePagesDeletedThisRun_AllRestoredInReverseOrder_OnRollback()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(Path.Combine(wikiRoot, "tech"));
            var firstPath = Path.Combine(wikiRoot, "tech", "first.md");
            var secondPath = Path.Combine(wikiRoot, "tech", "second.md");
            await File.WriteAllTextAsync(firstPath, "first content");
            await File.WriteAllTextAsync(secondPath, "second content");

            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writeRules: [new WriteRule(wikiRoot + Path.DirectorySeparatorChar, WriteMode.ReadWrite)],
                deleteRules: [new DeleteRule(wikiRoot + Path.DirectorySeparatorChar)]);
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, wikiRoot, registry: FullScopeRegistry);

            await executor.ExecuteAsync(
                ToolRegistry.DeleteFile, JsonSerializer.Serialize(new { path = "tech/first.md" }), turn: 1, CancellationToken.None);
            await executor.ExecuteAsync(
                ToolRegistry.DeleteFile, JsonSerializer.Serialize(new { path = "tech/second.md" }), turn: 2, CancellationToken.None);

            Assert.Equal(2, executor.DeletedPaths.Count);

            var rollback = await journal.RollbackAsync(CancellationToken.None);

            Assert.All(rollback.Values, Assert.True);
            Assert.Equal("first content", await File.ReadAllTextAsync(firstPath));
            Assert.Equal("second content", await File.ReadAllTextAsync(secondPath));
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task DeletingAFile_OutsideTheDeleteScope_IsDeniedAndFileRemains()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(Path.Combine(wikiRoot, "tech"));
            var pagePath = Path.Combine(wikiRoot, "tech", "protected.md");
            const string originalContent = "protected content";
            await File.WriteAllTextAsync(pagePath, originalContent);

            // read-write on the whole root, but no delete rule at all (e.g. Ingest's
            // shape) — R3: deletion is never granted by inheritance from the write scope.
            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writeRules: [new WriteRule(wikiRoot + Path.DirectorySeparatorChar, WriteMode.ReadWrite)]);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: FullScopeRegistry);

            var result = await executor.ExecuteAsync(
                ToolRegistry.DeleteFile,
                JsonSerializer.Serialize(new { path = "tech/protected.md" }),
                turn: 1, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("no_rule", Assert.Single(executor.Denials).Reason);
            Assert.True(File.Exists(pagePath));
            Assert.Equal(originalContent, await File.ReadAllTextAsync(pagePath));
            Assert.Empty(executor.DeletedPaths);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── shared setup ─────────────────────────────────────────────────────────────

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lint-deletion-rollback-{Guid.NewGuid():N}");
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
