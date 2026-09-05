using System.Diagnostics;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.OperationalState;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T014 (018-hub-cli-commands, research.md D1b): proves <see cref="OperationalStateRepository"/>'s
/// SQLite hardening (T012 — WAL journal mode + <c>busy_timeout</c>) lets a Hub-CLI dual-writer
/// pair back off and retry instead of failing with <c>SQLITE_BUSY</c>. The 018 feature removes
/// the assumption that only one process ever writes to the operational-state database at a
/// time (a running Hub and a CLI invocation now both can), so this is the "no global guard —
/// SQLite tolerates the race" counterpart to <see cref="CrossProcessFileLockTests"/>'s explicit
/// file-lock coverage of <c>lint.pid</c> (D1a, a true invariant that SQLite retry alone cannot
/// satisfy). Later phases may extend this file with further CLI-vs-Hub concurrency scenarios
/// (e.g. the cross-process <c>lint.pid</c> conflict test, T019; the telemetry flush test, T037).
/// </summary>
public class HubCliConcurrencyTests
{
    /// <summary>
    /// Deterministic proof: a second writer (standalone connection, standing in for "the
    /// other process" — Hub or CLI, direction doesn't matter) holds the SQLite write lock
    /// via an explicit <c>BEGIN IMMEDIATE</c> transaction for a fixed duration. Meanwhile
    /// the repository — a completely independent connection to the very same database
    /// file — attempts a write. Without <c>busy_timeout</c>, SQLite fails that write
    /// immediately with <c>SQLITE_BUSY</c> (error code 5); with it (T012), the repository's
    /// connection blocks and retries until the lock is released, then succeeds. The
    /// elapsed-time assertion is what distinguishes "waited for the lock" from "got lucky
    /// and never actually contended."
    /// </summary>
    [Fact]
    public async Task Write_WhileAnotherConnectionHoldsTheWriteLock_WaitsForRelease_InsteadOfFailingWithSqliteBusy()
    {
        var root = CreateTempDir("hub-cli-concurrency-block");
        var dbPath = Path.Combine(root, "operational-state.db");

        try
        {
            var repository = new OperationalStateRepository(dbPath);
            await repository.InitializeAsync();

            var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            await using var holderConnection = new SqliteConnection(connectionString);
            await holderConnection.OpenAsync();

            var walCommand = holderConnection.CreateCommand();
            walCommand.CommandText = "PRAGMA journal_mode = 'WAL';";
            await walCommand.ExecuteNonQueryAsync();

            var beginCommand = holderConnection.CreateCommand();
            beginCommand.CommandText = "BEGIN IMMEDIATE;";
            await beginCommand.ExecuteNonQueryAsync();

            var holdDuration = TimeSpan.FromMilliseconds(750);
            var releaseTask = ReleaseWriteLockAfterHoldDurationAsync(holderConnection, holdDuration);

            var taskId = "2026-08-03-concurrency-a1b2c3";
            var stopwatch = Stopwatch.StartNew();

            // Must not throw: the busy_timeout hardening (T012) makes this wait for the
            // holder's COMMIT above instead of surfacing SqliteException(SQLITE_BUSY).
            await repository.UpsertIngestTaskStateAsync(new IngestOperationalTaskState(taskId, "running", 4242, DateTimeOffset.UtcNow));
            stopwatch.Stop();

            await releaseTask;

            Assert.True(
                stopwatch.Elapsed >= holdDuration - TimeSpan.FromMilliseconds(150),
                $"The write returned after {stopwatch.Elapsed}, well before the {holdDuration} hold released its " +
                "lock — it did not genuinely wait for the other writer, so this test did not exercise busy_timeout.");

            var stored = await repository.GetIngestTaskStatesByStatusAsync("running");
            Assert.Contains(stored, row => row.TaskId == taskId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Holds <paramref name="holderConnection"/>'s write lock for a fixed
    /// <paramref name="holdDuration"/> before releasing it — the wait's subject (the elapsed
    /// hold time) is what the test above asserts on, so it is genuinely time-based rather than
    /// a substitute for a condition-based wait (ADR-021 FR-005).
    /// </summary>
    [Trait("TimingDependent", "true")]
    private static async Task ReleaseWriteLockAfterHoldDurationAsync(SqliteConnection holderConnection, TimeSpan holdDuration)
    {
        await Task.Delay(holdDuration);
        var commitCommand = holderConnection.CreateCommand();
        commitCommand.CommandText = "COMMIT;";
        await commitCommand.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// T019 (018-hub-cli-commands, US1, research.md D1a): two entirely separate
    /// <see cref="LintRunCoordinator"/> instances — standing in for "a running Hub" and
    /// "a CLI invocation" (or two Hub-adjacent processes) — pointed at the SAME resolved
    /// paths (same <c>lint.pid</c> file), proving the T016 cross-process lock, not just
    /// the in-process <c>_slot</c> semaphore each coordinator instance owns privately.
    /// Coordinator A triggers and holds the lock for a scripted, non-instant run (not yet
    /// terminal); coordinator B's own <c>TriggerAsync</c> — which would otherwise see an
    /// entirely free in-process slot, since it is a different instance — must still
    /// observe the conflict via the shared <c>lint.pid</c> file and return
    /// <see cref="LintSubmissionResult.Busy"/>, never spawning an agent.
    /// </summary>
    [Fact]
    public async Task LintPid_TwoSeparateCoordinatorInstances_SecondObservesBusy_WhileFirstHoldsTheCrossProcessLock()
    {
        var root = CreateTempDir("hub-cli-lint-pid-conflict");

        try
        {
            var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
            Directory.CreateDirectory(paths.FindingsDir);

            // Coordinator A: the "first process" — a scripted run that stays active long
            // enough for coordinator B's trigger attempt to land while it still holds the
            // lock, but short enough to keep the test fast.
            var launcherA = new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromSeconds(1));
            var coordinatorA = new LintRunCoordinator(
                launcherA,
                new LintFindingsReportStore(paths, NullLogger<LintFindingsReportStore>.Instance),
                paths,
                logger: NullLogger<LintRunCoordinator>.Instance);

            // Coordinator B: an entirely separate instance (own in-process `_slot`,
            // never contended by A) — the same shape a second Hub/CLI process would have.
            var launcherB = new FakeAgentProcessLauncher();
            var coordinatorB = new LintRunCoordinator(
                launcherB,
                new LintFindingsReportStore(paths, NullLogger<LintFindingsReportStore>.Instance),
                paths,
                logger: NullLogger<LintRunCoordinator>.Instance);

            var resultA = await coordinatorA.TriggerAsync();
            Assert.IsType<LintSubmissionResult.Accepted>(resultA);

            var resultB = await coordinatorB.TriggerAsync();
            Assert.IsType<LintSubmissionResult.Busy>(resultB);

            // The conflict was detected before any dispatch — B's launcher never saw a request.
            Assert.Empty(launcherB.LintRequests);

            // Let A's scripted run wind down and release the lock before the temp
            // directory is torn down, so its background completion (FinishRunAsync's
            // Findings Report write) doesn't race the cleanup below.
            await PollAsync.WaitAsync(
                () => !coordinatorA.IsRunActive,
                TimeSpan.FromSeconds(5),
                "Coordinator A's scripted run did not wind down and release the lint.pid lock within the timeout.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
