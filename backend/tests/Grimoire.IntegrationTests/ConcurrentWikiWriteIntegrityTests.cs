using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Guardrails.Coordination;
using Grimoire.Domain.Guardrails;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T036-T039 (012-query-synthesis-writes, US3, ADR-015, research.md R6): concurrent
/// wiki-writing activity (synthesis-preserving Query turns and Ingest, in any combination)
/// must never corrupt <c>index.md</c>/<c>log.md</c>/pages (FR-009/SC-003), interruption
/// must never roll back completed writes (FR-011), and write coordination must never block
/// a turn long enough to threaten streaming/interruption responsiveness (FR-010).
/// </summary>
public class ConcurrentWikiWriteIntegrityTests
{
    private static string HarnessDllPath =>
        Path.Combine(AppContext.BaseDirectory, "Grimoire.WriteLockTestHarness.dll");

    // ── T036: in-process concurrent writers, real GuardedToolExecutor/guard stack ────────

    [Fact]
    public async Task ThreeConcurrentWriters_TwoSynthesisPagesPlusIngestStyleAppends_AllEntriesPreserved_NoLostUpdates()
    {
        var root = CreateTempRoot("cwwi-t036");
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            var writeLocksDir = Path.Combine(root, "write-locks");
            Directory.CreateDirectory(Path.Combine(wikiRoot, "tech"));
            var indexPath = Path.Combine(wikiRoot, "index.md");
            var logPath = Path.Combine(wikiRoot, "log.md");
            await File.WriteAllTextAsync(indexPath, "# Wiki Index");
            await File.WriteAllTextAsync(logPath, "## Log");

            // Three separate GuardedToolExecutor instances — one per concurrent writer,
            // exactly matching production's one-guard-per-run lifecycle (ADR-015) — all
            // sharing the same wikiRoot/writeLocksDir, as three concurrent OS processes
            // would.
            var writerA = BuildExecutor(wikiRoot, writeLocksDir); // Query synthesis turn A
            var writerB = BuildExecutor(wikiRoot, writeLocksDir); // Query synthesis turn B
            var writerC = BuildExecutor(wikiRoot, writeLocksDir); // Ingest-style writer

            async Task RunSynthesisWriterAsync(GuardedToolExecutor executor, string pageRelativePath, string entryMarker)
            {
                var pageResult = await executor.ExecuteAsync(
                    ToolRegistry.WriteFile,
                    JsonSerializer.Serialize(new { path = pageRelativePath, content = $"# {entryMarker}" }),
                    turn: 1,
                    CancellationToken.None);
                Assert.False(pageResult.IsError, $"Unexpected denial creating {pageRelativePath}: {pageResult.Content}");

                await AppendWithRetryAsync(executor, "index.md", entryMarker);
                await AppendWithRetryAsync(executor, "log.md", entryMarker);
            }

            async Task RunIngestStyleWriterAsync(GuardedToolExecutor executor, string entryMarker)
            {
                await AppendWithRetryAsync(executor, "index.md", entryMarker);
                await AppendWithRetryAsync(executor, "log.md", entryMarker);
            }

            await Task.WhenAll(
                RunSynthesisWriterAsync(writerA, "tech/synthesis-a.md", "entry-A"),
                RunSynthesisWriterAsync(writerB, "tech/synthesis-b.md", "entry-B"),
                RunIngestStyleWriterAsync(writerC, "entry-C"));

            // Both new pages are complete.
            Assert.Equal("# entry-A", await File.ReadAllTextAsync(Path.Combine(wikiRoot, "tech", "synthesis-a.md")));
            Assert.Equal("# entry-B", await File.ReadAllTextAsync(Path.Combine(wikiRoot, "tech", "synthesis-b.md")));

            // No index/log entry was lost to a lost update.
            var finalIndex = await File.ReadAllTextAsync(indexPath);
            var finalLog = await File.ReadAllTextAsync(logPath);
            foreach (var marker in new[] { "entry-A", "entry-B", "entry-C" })
            {
                Assert.Contains(marker, finalIndex, StringComparison.Ordinal);
                Assert.Contains(marker, finalLog, StringComparison.Ordinal);
            }

            // Every denial any writer saw was a compare-and-swap rejection (the expected,
            // recoverable contention outcome) followed by a successful retry — never a
            // create-only or out-of-scope surprise.
            foreach (var executor in new[] { writerA, writerB, writerC })
            {
                Assert.All(executor.Denials, denial => Assert.Equal("write_conflict_stale_read", denial.Reason));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── T037: real multi-process variant (research.md R6) ────────────────────────────────

    [Fact]
    public async Task TwoRealSeparateProcesses_RacingSameIndexFile_LoserDeniedThenRetrySucceeds_BothEntriesPreserved()
    {
        var root = CreateTempRoot("cwwi-t037");
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            var writeLocksDir = Path.Combine(root, "write-locks");
            Directory.CreateDirectory(wikiRoot);
            var indexPath = Path.Combine(wikiRoot, "index.md");
            await File.WriteAllTextAsync(indexPath, "seed line");

            // Both processes read the original content before either writes — guarantees
            // genuine contention rather than relying on incidental timing.
            using var procA = StartGuardedAppendHarness(wikiRoot, writeLocksDir, "index.md", "entry-from-A", waitForStdinBeforeWrite: true);
            using var procB = StartGuardedAppendHarness(wikiRoot, writeLocksDir, "index.md", "entry-from-B", waitForStdinBeforeWrite: true);

            Assert.Equal("READ", await ReadLineAsync(procA));
            Assert.Equal("READ", await ReadLineAsync(procB));

            await procA.StandardInput.WriteLineAsync("go");
            var resultA = await ReadLineAsync(procA);
            await procA.WaitForExitAsync();
            Assert.Equal("WRITTEN", resultA);
            Assert.Equal(0, procA.ExitCode);

            await procB.StandardInput.WriteLineAsync("go");
            var resultB = await ReadLineAsync(procB);
            await procB.WaitForExitAsync();
            Assert.Equal("DENIED:write_conflict_stale_read", resultB);
            Assert.Equal(1, procB.ExitCode);

            var afterFirstRound = await File.ReadAllTextAsync(indexPath);
            Assert.Contains("entry-from-A", afterFirstRound, StringComparison.Ordinal);
            Assert.DoesNotContain("entry-from-B", afterFirstRound, StringComparison.Ordinal);

            // The loser's own agent loop would re-read and retry on a tool error exactly
            // like this one (ADR-015) — model that retry as a third real process.
            using var procC = StartGuardedAppendHarness(wikiRoot, writeLocksDir, "index.md", "entry-from-B-retry", waitForStdinBeforeWrite: false);
            Assert.Equal("READ", await ReadLineAsync(procC));
            var resultC = await ReadLineAsync(procC);
            await procC.WaitForExitAsync();
            Assert.Equal("WRITTEN", resultC);
            Assert.Equal(0, procC.ExitCode);

            var finalContent = await File.ReadAllTextAsync(indexPath);
            Assert.Contains("entry-from-A", finalContent, StringComparison.Ordinal);
            Assert.Contains("entry-from-B-retry", finalContent, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── T038: interruption never rolls back completed writes; lock is released ───────────

    [Fact]
    public async Task InterruptionAfterSuccessfulWrite_PriorWritesRemain_AndTheInFlightTargetsLockIsReleased()
    {
        var root = CreateTempRoot("cwwi-t038");
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            var writeLocksDir = Path.Combine(root, "write-locks");
            var techDir = Path.Combine(wikiRoot, "tech");
            var trapDir = Path.Combine(wikiRoot, "trap");
            Directory.CreateDirectory(techDir);
            var indexPath = Path.Combine(wikiRoot, "index.md");
            await File.WriteAllTextAsync(indexPath, "# Wiki Index");

            var techPrefix = techDir + Path.DirectorySeparatorChar;
            var trapPrefix = trapDir + Path.DirectorySeparatorChar;
            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writeRules:
                [
                    new WriteRule(techPrefix, CreateOnly: true),
                    new WriteRule(indexPath, CreateOnly: false),
                    new WriteRule(trapPrefix, CreateOnly: false),
                ]);
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, wikiRoot, writeLocksDir: writeLocksDir);

            // 1. A synthesis turn successfully creates a new page — this is the write that
            // completed "immediately before the turn was interrupted" (FR-011's scenario).
            var pageResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "tech/synthesis.md", content = "# Preserved insight" }),
                turn: 1,
                CancellationToken.None);
            Assert.False(pageResult.IsError);
            var pagePath = Path.Combine(techDir, "synthesis.md");
            Assert.True(File.Exists(pagePath));

            // 2. Before the turn reaches its terminal state, its *next* write hits a real
            // filesystem fault deep inside the guarded critical section (a plain file
            // already occupies the name "trap" where a directory needs to be created) —
            // this deterministically reproduces "interrupted mid-write" without relying on
            // timing, while still exercising the executor's real try/finally lock-release
            // path (not a test double standing in for it).
            await File.WriteAllTextAsync(trapDir, "not a directory");
            var trapTarget = Path.Combine(trapDir, "child.md");

            var thrown = await Record.ExceptionAsync(() => executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "trap/child.md", content = "never lands" }),
                turn: 2,
                CancellationToken.None));

            Assert.NotNull(thrown);
            Assert.IsType<IOException>(thrown);

            // 3. FR-011: the page created before the interruption remains, untouched.
            Assert.True(File.Exists(pagePath));
            Assert.Equal("# Preserved insight", await File.ReadAllTextAsync(pagePath));

            // 4. The write-coordination lock for the in-flight (failed) target was released
            // in the executor's `finally` regardless of the exception — a subsequent writer
            // can still acquire it immediately, proving the run cannot wedge the target.
            var reacquired = await CrossProcessFileLock.TryAcquireAsync(
                writeLocksDir, trapTarget, TimeSpan.FromMilliseconds(500), CancellationToken.None);
            Assert.NotNull(reacquired);
            reacquired!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── T039: bounded lock-wait time under contention (FR-010) ───────────────────────────

    [Fact]
    public async Task UnderContention_LockWaitTimePerAttempt_StaysBounded_NeverApproachesSeconds()
    {
        var root = CreateTempRoot("cwwi-t039");
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            var writeLocksDir = Path.Combine(root, "write-locks");
            Directory.CreateDirectory(Path.Combine(wikiRoot, "tech"));
            var indexPath = Path.Combine(wikiRoot, "index.md");
            var logPath = Path.Combine(wikiRoot, "log.md");
            await File.WriteAllTextAsync(indexPath, "# Wiki Index");
            await File.WriteAllTextAsync(logPath, "## Log");

            var spy = new WriteLockWaitSpy();
            var writers = Enumerable.Range(0, 5)
                .Select(_ => BuildExecutor(wikiRoot, writeLocksDir, spy))
                .ToArray();

            var overallStopwatch = Stopwatch.StartNew();

            await Task.WhenAll(writers.Select((executor, i) => Task.Run(async () =>
            {
                var pageRelativePath = $"tech/synthesis-{i}.md";
                var entryMarker = $"entry-{i}";

                var pageResult = await executor.ExecuteAsync(
                    ToolRegistry.WriteFile,
                    JsonSerializer.Serialize(new { path = pageRelativePath, content = $"# {entryMarker}" }),
                    turn: 1,
                    CancellationToken.None);
                Assert.False(pageResult.IsError);

                await AppendWithRetryAsync(executor, "index.md", entryMarker);
                await AppendWithRetryAsync(executor, "log.md", entryMarker);
            })));

            overallStopwatch.Stop();

            // Five concurrent writers contending for the same two shared targets still
            // finish in well under a second, total — nowhere near the 5-second default
            // backoff cap, and no single acquisition attempt is ever blocked long enough to
            // threaten a streaming turn's responsiveness.
            Assert.True(overallStopwatch.Elapsed < TimeSpan.FromSeconds(5),
                $"Contention scenario took {overallStopwatch.Elapsed} — expected well under 5s.");

            Assert.NotEmpty(spy.WaitSeconds);
            Assert.All(spy.WaitSeconds, waitSeconds =>
                Assert.True(waitSeconds < 1.0, $"A single lock acquisition waited {waitSeconds * 1000:F0}ms — expected sub-second."));
            Assert.DoesNotContain("timeout", spy.Outcomes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────

    private sealed class WriteLockWaitSpy : IToolCallInstrumentation
    {
        private readonly ConcurrentBag<double> _waitSeconds = [];
        private readonly ConcurrentBag<string> _outcomes = [];

        public IReadOnlyCollection<double> WaitSeconds => _waitSeconds;
        public IReadOnlyCollection<string> Outcomes => _outcomes;

        public void RecordAllowed(string taskId, string tool, string target, int turn) { }
        public void RecordDenied(string taskId, string tool, string requestedTarget, string canonicalTarget, string reason, int turn) { }

        public void RecordWriteLockAcquisition(string taskId, string path, string outcome, double waitSeconds, int turn)
        {
            _waitSeconds.Add(waitSeconds);
            _outcomes.Add(outcome);
        }
    }

    // 019-fast-test-tier (ADR-021 R4): the jittered pause between retries is an inherent
    // livelock-avoidance mechanism of the concurrent-retry behavior under test, not a proxy
    // for an async operation's completion — there is no condition to poll for between
    // attempts. Exempt from the fixed-wait ban (FR-005).
    [Trait("TimingDependent", "true")]
    private static async Task AppendWithRetryAsync(
        GuardedToolExecutor executor, string relativePath, string entryMarker, int maxAttempts = 100)
    {
        var random = new Random(entryMarker.GetHashCode());

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var readResult = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = relativePath }),
                turn: attempt * 2 + 1,
                CancellationToken.None);
            var current = readResult.IsError ? string.Empty : readResult.Content;

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = relativePath, content = current + "\n" + entryMarker }),
                turn: attempt * 2 + 2,
                CancellationToken.None);

            if (!writeResult.IsError)
            {
                return;
            }

            if (writeResult.Content.Contains("write_conflict_stale_read", StringComparison.Ordinal))
            {
                // Real agents re-read and retry after a tool error (ADR-015) — a small
                // jittered pause between attempts avoids a tight-loop livelock among
                // several equally-fast concurrent retriers hammering the same target.
                await Task.Delay(random.Next(1, 15));
                continue;
            }

            throw new InvalidOperationException($"Unexpected denial appending to {relativePath}: {writeResult.Content}");
        }

        throw new TimeoutException($"Exceeded {maxAttempts} retry attempts appending to {relativePath}.");
    }

    private static GuardedToolExecutor BuildExecutor(
        string wikiRoot, string writeLocksDir, IToolCallInstrumentation? instrumentation = null)
    {
        var techPrefix = Path.Combine(wikiRoot, "tech") + Path.DirectorySeparatorChar;
        var indexPath = Path.Combine(wikiRoot, "index.md");
        var logPath = Path.Combine(wikiRoot, "log.md");

        var policy = new SafetyPolicy(
            wikiRoot,
            readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
            writeRules:
            [
                new WriteRule(techPrefix, CreateOnly: true),
                new WriteRule(indexPath, CreateOnly: false),
                new WriteRule(logPath, CreateOnly: false),
            ]);

        var journal = new WriteJournal();
        return new GuardedToolExecutor(
            policy, journal, wikiRoot, writeLocksDir: writeLocksDir, instrumentation: instrumentation);
    }

    private static Process StartGuardedAppendHarness(
        string wikiRoot, string writeLocksDir, string relativePath, string entryText, bool waitForStdinBeforeWrite)
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
        startInfo.ArgumentList.Add("guarded-append");
        startInfo.ArgumentList.Add(wikiRoot);
        startInfo.ArgumentList.Add(writeLocksDir);
        startInfo.ArgumentList.Add(relativePath);
        startInfo.ArgumentList.Add(entryText);
        startInfo.ArgumentList.Add(waitForStdinBeforeWrite ? "1" : "0");

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start write-lock test harness process.");
    }

    private static async Task<string?> ReadLineAsync(Process process)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await process.StandardOutput.ReadLineAsync(cts.Token);
    }

    private static string CreateTempRoot(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
