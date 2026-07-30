using Grimoire.AgentRuntime.Guardrails.Coordination;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T015 (012-query-synthesis-writes, ADR-015): <see cref="SharedFileWriteGuard"/>'s
/// create-only/compare-and-swap decision logic in isolation (contract §3,
/// data-model.md state transition), hermetic against a real temp filesystem.
/// </summary>
public class SharedFileWriteGuardTests
{
    private static SharedFileWriteGuard NewGuard(string writeLocksDir) =>
        new(writeLocksDir, backoffCap: TimeSpan.FromMilliseconds(500));

    [Fact]
    public async Task CreateOnlyWrite_ToNonExistentPath_Allows()
    {
        var root = CreateTempDir();
        try
        {
            var guard = NewGuard(Path.Combine(root, "write-locks"));
            var target = Path.Combine(root, "pages", "new.md");

            var decision = await guard.EvaluateWriteAsync(target, isCreateOnly: true, CancellationToken.None);

            Assert.True(decision.IsAllowed);
            Assert.NotNull(decision.LockHandle);
            decision.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreateOnlyWrite_ToExistingPath_Denies_CreateOnlyTargetExists()
    {
        var root = CreateTempDir();
        try
        {
            var target = Path.Combine(root, "pages", "existing.md");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, "already here");

            var guard = NewGuard(Path.Combine(root, "write-locks"));
            var decision = await guard.EvaluateWriteAsync(target, isCreateOnly: true, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("create_only_target_exists", decision.DenialReason);
            Assert.Null(decision.LockHandle);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadWriteWrite_ToPathNeverReadAndNotOnDisk_Allows_BrandNewFile()
    {
        var root = CreateTempDir();
        try
        {
            var guard = NewGuard(Path.Combine(root, "write-locks"));
            var target = Path.Combine(root, "pages", "brand-new.md");

            var decision = await guard.EvaluateWriteAsync(target, isCreateOnly: false, CancellationToken.None);

            Assert.True(decision.IsAllowed);
            decision.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadWriteWrite_ToPathThisRunRead_UnmodifiedSince_Allows()
    {
        var root = CreateTempDir();
        try
        {
            var target = Path.Combine(root, "index.md");
            await File.WriteAllTextAsync(target, "original content");

            var guard = NewGuard(Path.Combine(root, "write-locks"));
            guard.OnReadFile(target, await File.ReadAllTextAsync(target));

            var decision = await guard.EvaluateWriteAsync(target, isCreateOnly: false, CancellationToken.None);

            Assert.True(decision.IsAllowed);
            decision.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadWriteWrite_ToPathThisRunRead_ModifiedByConcurrentWriterSince_Denies_WriteConflictStaleRead()
    {
        var root = CreateTempDir();
        try
        {
            var target = Path.Combine(root, "index.md");
            await File.WriteAllTextAsync(target, "original content");

            var guard = NewGuard(Path.Combine(root, "write-locks"));
            guard.OnReadFile(target, "original content");

            // A concurrent writer (another process/run) changes the file after this run read it.
            await File.WriteAllTextAsync(target, "concurrently modified content");

            var decision = await guard.EvaluateWriteAsync(target, isCreateOnly: false, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("write_conflict_stale_read", decision.DenialReason);
            Assert.Null(decision.LockHandle);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadWriteWrite_ToExistingPathThisRunNeverRead_Denies_WriteConflictStaleRead()
    {
        // No baseline to compare against (never read, never written by this run) — the
        // guard cannot tell whether the on-disk content is what this run expects, so it
        // fails closed rather than silently clobbering it (data-model.md decision tree).
        var root = CreateTempDir();
        try
        {
            var target = Path.Combine(root, "index.md");
            await File.WriteAllTextAsync(target, "content nobody in this run has read");

            var guard = NewGuard(Path.Combine(root, "write-locks"));
            var decision = await guard.EvaluateWriteAsync(target, isCreateOnly: false, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("write_conflict_stale_read", decision.DenialReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SecondWrite_ToPathThisRunCreatedItself_Allows_ViaOnWriteCommitted()
    {
        var root = CreateTempDir();
        try
        {
            var target = Path.Combine(root, "pages", "created-by-this-run.md");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var guard = NewGuard(Path.Combine(root, "write-locks"));

            // First write: brand-new file, allowed.
            var first = await guard.EvaluateWriteAsync(target, isCreateOnly: false, CancellationToken.None);
            Assert.True(first.IsAllowed);
            await File.WriteAllTextAsync(target, "first content");
            guard.OnWriteCommitted(target, "first content");
            first.LockHandle!.Dispose();

            // Second write to the same path in the same run: allowed without a prior
            // read_file, because OnWriteCommitted updated the baseline.
            var second = await guard.EvaluateWriteAsync(target, isCreateOnly: false, CancellationToken.None);
            Assert.True(second.IsAllowed);
            second.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LockAcquisitionTimeout_Denies_WriteCoordinationTimeout()
    {
        var root = CreateTempDir();
        try
        {
            var writeLocksDir = Path.Combine(root, "write-locks");
            var target = Path.Combine(root, "index.md");

            var externalHolder = await CrossProcessFileLock.TryAcquireAsync(
                writeLocksDir, target, TimeSpan.FromMilliseconds(500), CancellationToken.None);
            Assert.NotNull(externalHolder);

            var guard = new SharedFileWriteGuard(writeLocksDir, backoffCap: TimeSpan.FromMilliseconds(200));
            var decision = await guard.EvaluateWriteAsync(target, isCreateOnly: false, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("write_coordination_timeout", decision.DenialReason);

            externalHolder!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"shared-write-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
