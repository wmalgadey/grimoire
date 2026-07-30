using Grimoire.AgentRuntime.Guardrails.Coordination;
using Grimoire.Domain.Guardrails;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T010 (013-lint-agent, ADR-016): <see cref="SharedFileWriteGuard"/>'s frontmatter/body
/// split check for <see cref="WriteMode.FrontmatterOnly"/>, in isolation, hermetic against
/// a real temp filesystem. Composes with (never replaces) the existing compare-and-swap
/// check exercised by <see cref="SharedFileWriteGuardTests"/>, which this test file leaves
/// entirely unmodified.
/// </summary>
public class SharedFileWriteGuardFrontmatterOnlyTests
{
    private const string SamplePage = """
        ---
        title: Sample
        tags: []
        ---
        # Sample

        Body content, unchanged.
        """;

    private static SharedFileWriteGuard NewGuard(string writeLocksDir) =>
        new(writeLocksDir, backoffCap: TimeSpan.FromMilliseconds(500));

    [Fact]
    public async Task FrontmatterOnlyWrite_ToNonExistentTarget_Denies_FrontmatterOnlyTargetMissing()
    {
        var root = CreateTempDir();
        try
        {
            var guard = NewGuard(Path.Combine(root, "write-locks"));
            var target = Path.Combine(root, "pages", "missing.md");

            var decision = await guard.EvaluateWriteAsync(
                target, WriteMode.FrontmatterOnly, SamplePage, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("frontmatter_only_target_missing", decision.DenialReason);
            Assert.Null(decision.LockHandle);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FrontmatterOnlyWrite_IdenticalBody_ChangedFrontmatter_Allows()
    {
        var root = CreateTempDir();
        try
        {
            var target = Path.Combine(root, "pages", "existing.md");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, SamplePage);

            var guard = NewGuard(Path.Combine(root, "write-locks"));
            guard.OnReadFile(target, SamplePage);

            var proposed = """
                ---
                title: Sample
                tags: [updated]
                inbound_links: 3
                ---
                # Sample

                Body content, unchanged.
                """;

            var decision = await guard.EvaluateWriteAsync(
                target, WriteMode.FrontmatterOnly, proposed, CancellationToken.None);

            Assert.True(decision.IsAllowed);
            decision.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FrontmatterOnlyWrite_ChangedBody_Denies_FrontmatterOnlyBodyChanged()
    {
        var root = CreateTempDir();
        try
        {
            var target = Path.Combine(root, "pages", "existing.md");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, SamplePage);

            var guard = NewGuard(Path.Combine(root, "write-locks"));
            guard.OnReadFile(target, SamplePage);

            var proposed = """
                ---
                title: Sample
                ---
                # Sample

                Body content, CHANGED.
                """;

            var decision = await guard.EvaluateWriteAsync(
                target, WriteMode.FrontmatterOnly, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("frontmatter_only_body_changed", decision.DenialReason);
            Assert.Null(decision.LockHandle);

            // Nothing was applied — the on-disk page is untouched.
            Assert.Equal(SamplePage, await File.ReadAllTextAsync(target));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FrontmatterOnlyWrite_ProposedContentLacksFrontmatter_Denies_FrontmatterOnlyMalformedDocument()
    {
        var root = CreateTempDir();
        try
        {
            var target = Path.Combine(root, "pages", "existing.md");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, SamplePage);

            var guard = NewGuard(Path.Combine(root, "write-locks"));
            guard.OnReadFile(target, SamplePage);

            var proposed = "# Sample\n\nNo frontmatter block at all.";

            var decision = await guard.EvaluateWriteAsync(
                target, WriteMode.FrontmatterOnly, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("frontmatter_only_malformed_document", decision.DenialReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FrontmatterOnlyWrite_CurrentOnDiskContentLacksFrontmatter_Denies_FrontmatterOnlyMalformedDocument()
    {
        var root = CreateTempDir();
        try
        {
            var target = Path.Combine(root, "pages", "existing.md");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var malformedOnDisk = "not a frontmatter document";
            await File.WriteAllTextAsync(target, malformedOnDisk);

            var guard = NewGuard(Path.Combine(root, "write-locks"));
            guard.OnReadFile(target, malformedOnDisk);

            var decision = await guard.EvaluateWriteAsync(
                target, WriteMode.FrontmatterOnly, SamplePage, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("frontmatter_only_malformed_document", decision.DenialReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FrontmatterOnlyWrite_OnlyOneDelimiter_Denies_FrontmatterOnlyMalformedDocument()
    {
        var root = CreateTempDir();
        try
        {
            var target = Path.Combine(root, "pages", "existing.md");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var onlyOpeningDelimiter = "---\ntitle: no closing delimiter\n# Body\n";
            await File.WriteAllTextAsync(target, onlyOpeningDelimiter);

            var guard = NewGuard(Path.Combine(root, "write-locks"));
            guard.OnReadFile(target, onlyOpeningDelimiter);

            var decision = await guard.EvaluateWriteAsync(
                target, WriteMode.FrontmatterOnly, SamplePage, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("frontmatter_only_malformed_document", decision.DenialReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FrontmatterOnlyWrite_StaleRead_Denies_WriteConflictStaleRead_EvenWhenBodyWouldOtherwiseMatch()
    {
        var root = CreateTempDir();
        try
        {
            var target = Path.Combine(root, "pages", "existing.md");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, SamplePage);

            var guard = NewGuard(Path.Combine(root, "write-locks"));
            guard.OnReadFile(target, SamplePage);

            // A concurrent writer changes the file after this run read it — the
            // compare-and-swap check must deny before the frontmatter/body check is ever
            // reached, even though the concurrently-written content's body happens to still
            // match what this run is about to propose.
            var concurrentlyWritten = """
                ---
                title: Sample
                tags: [concurrently-changed]
                ---
                # Sample

                Body content, unchanged.
                """;
            await File.WriteAllTextAsync(target, concurrentlyWritten);

            var proposed = """
                ---
                title: Sample
                tags: [this-runs-proposal]
                ---
                # Sample

                Body content, unchanged.
                """;

            var decision = await guard.EvaluateWriteAsync(
                target, WriteMode.FrontmatterOnly, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("write_conflict_stale_read", decision.DenialReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"shared-write-guard-frontmatter-only-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
