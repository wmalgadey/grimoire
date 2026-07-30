using System.Diagnostics;
using Grimoire.AgentRuntime.Guardrails.Coordination;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T013 (012-query-synthesis-writes, ADR-015, research.md R6): in-process behavior of
/// <see cref="CrossProcessFileLock"/> plus a genuine multi-process test spawning two real
/// separate <c>Grimoire.WriteLockTestHarness</c> processes racing the same lock file — an
/// in-process fake alone cannot prove real cross-process OS-level exclusion.
/// </summary>
public class CrossProcessFileLockTests
{
    private static string HarnessDllPath =>
        Path.Combine(AppContext.BaseDirectory, "Grimoire.WriteLockTestHarness.dll");

    // ── In-process behavior ──────────────────────────────────────────────────────

    [Fact]
    public async Task SecondAcquisition_OnSamePath_FailsWhileFirstHolderHasItOpen_AndSucceedsAfterRelease()
    {
        var writeLocksDir = CreateTempDir("cpfl-inproc");
        var targetPath = Path.Combine(writeLocksDir, "target.md");

        try
        {
            var first = await CrossProcessFileLock.TryAcquireAsync(
                writeLocksDir, targetPath, TimeSpan.FromMilliseconds(500), CancellationToken.None);
            Assert.NotNull(first);

            var second = await CrossProcessFileLock.TryAcquireAsync(
                writeLocksDir, targetPath, TimeSpan.FromMilliseconds(200), CancellationToken.None);
            Assert.Null(second);

            first!.Dispose();

            var third = await CrossProcessFileLock.TryAcquireAsync(
                writeLocksDir, targetPath, TimeSpan.FromMilliseconds(500), CancellationToken.None);
            Assert.NotNull(third);
            third!.Dispose();
        }
        finally
        {
            Directory.Delete(writeLocksDir, recursive: true);
        }
    }

    [Fact]
    public async Task AcquisitionPastBackoffCap_ReturnsTimeoutResult_WithinBoundedWallClockWindow()
    {
        var writeLocksDir = CreateTempDir("cpfl-timeout");
        var targetPath = Path.Combine(writeLocksDir, "target.md");

        try
        {
            var holder = await CrossProcessFileLock.TryAcquireAsync(
                writeLocksDir, targetPath, TimeSpan.FromMilliseconds(500), CancellationToken.None);
            Assert.NotNull(holder);

            var backoffCap = TimeSpan.FromMilliseconds(300);
            var stopwatch = Stopwatch.StartNew();
            var result = await CrossProcessFileLock.TryAcquireAsync(
                writeLocksDir, targetPath, backoffCap, CancellationToken.None);
            stopwatch.Stop();

            Assert.Null(result);
            // Bounded: not instant (some polling happened) and not wildly over the cap.
            Assert.True(stopwatch.Elapsed >= backoffCap - TimeSpan.FromMilliseconds(50));
            Assert.True(stopwatch.Elapsed < backoffCap + TimeSpan.FromSeconds(2));

            holder!.Dispose();
        }
        finally
        {
            Directory.Delete(writeLocksDir, recursive: true);
        }
    }

    [Fact]
    public async Task DifferentTargetPaths_DoNotContendForTheSameLockFile()
    {
        var writeLocksDir = CreateTempDir("cpfl-distinct-targets");

        try
        {
            var lockA = await CrossProcessFileLock.TryAcquireAsync(
                writeLocksDir, Path.Combine(writeLocksDir, "a.md"), TimeSpan.FromMilliseconds(500), CancellationToken.None);
            var lockB = await CrossProcessFileLock.TryAcquireAsync(
                writeLocksDir, Path.Combine(writeLocksDir, "b.md"), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            Assert.NotNull(lockA);
            Assert.NotNull(lockB);

            lockA!.Dispose();
            lockB!.Dispose();
        }
        finally
        {
            Directory.Delete(writeLocksDir, recursive: true);
        }
    }

    // ── Real multi-process behavior (research.md R6) ─────────────────────────────

    [Fact]
    public async Task TwoRealSeparateProcesses_RacingSameTargetPath_NeverBothReportAcquired()
    {
        var writeLocksDir = CreateTempDir("cpfl-mp-race");
        var targetPath = Path.Combine(writeLocksDir, "index.md");

        try
        {
            // Process A holds the lock for 1500ms; process B (started right after
            // confirming A has acquired) has only a 300ms backoff cap — it must time out,
            // never a second "ACQUIRED".
            using var procA = StartHarness(writeLocksDir, targetPath, backoffCapMs: 5000, holdMs: 1500);
            var firstLineA = await ReadLineAsync(procA);
            Assert.Equal("ACQUIRED", firstLineA);

            using var procB = StartHarness(writeLocksDir, targetPath, backoffCapMs: 300, holdMs: 0);
            var firstLineB = await ReadLineAsync(procB);
            Assert.Equal("TIMEOUT", firstLineB);

            await procB.WaitForExitAsync();
            Assert.Equal(1, procB.ExitCode);

            // After A releases, a third real process must be able to acquire immediately.
            await procA.WaitForExitAsync();
            Assert.Equal(0, procA.ExitCode);

            using var procC = StartHarness(writeLocksDir, targetPath, backoffCapMs: 2000, holdMs: 0);
            var firstLineC = await ReadLineAsync(procC);
            Assert.Equal("ACQUIRED", firstLineC);
            await procC.WaitForExitAsync();
            Assert.Equal(0, procC.ExitCode);
        }
        finally
        {
            Directory.Delete(writeLocksDir, recursive: true);
        }
    }

    [Fact]
    public async Task LockHeldByKilledProcess_IsAcquirableByANewAttempt()
    {
        var writeLocksDir = CreateTempDir("cpfl-mp-kill");
        var targetPath = Path.Combine(writeLocksDir, "index.md");

        try
        {
            // holdMs < 0: hold indefinitely until killed.
            using var holderProcess = StartHarness(writeLocksDir, targetPath, backoffCapMs: 5000, holdMs: -1);
            var firstLine = await ReadLineAsync(holderProcess);
            Assert.Equal("ACQUIRED", firstLine);

            holderProcess.Kill(entireProcessTree: true);
            holderProcess.WaitForExit(5_000);

            // The OS releases the file lock on process death — a fresh attempt (in-process
            // this time) must succeed without needing the cap.
            var reacquired = await CrossProcessFileLock.TryAcquireAsync(
                writeLocksDir, targetPath, TimeSpan.FromMilliseconds(2000), CancellationToken.None);

            Assert.NotNull(reacquired);
            reacquired!.Dispose();
        }
        finally
        {
            Directory.Delete(writeLocksDir, recursive: true);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static Process StartHarness(string writeLocksDir, string targetPath, int backoffCapMs, int holdMs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(HarnessDllPath);
        startInfo.ArgumentList.Add("lock-probe");
        startInfo.ArgumentList.Add(writeLocksDir);
        startInfo.ArgumentList.Add(targetPath);
        startInfo.ArgumentList.Add(backoffCapMs.ToString());
        startInfo.ArgumentList.Add(holdMs.ToString());

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start write-lock test harness process.");
    }

    private static async Task<string?> ReadLineAsync(Process process)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await process.StandardOutput.ReadLineAsync(cts.Token);
    }

    private static string CreateTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
