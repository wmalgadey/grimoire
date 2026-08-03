using System.Diagnostics;
using Grimoire.Hub.OperationalState;
using Microsoft.Data.Sqlite;

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
            var releaseTask = Task.Run(async () =>
            {
                await Task.Delay(holdDuration);
                var commitCommand = holderConnection.CreateCommand();
                commitCommand.CommandText = "COMMIT;";
                await commitCommand.ExecuteNonQueryAsync();
            });

            var taskId = "2026-08-03-concurrency-a1b2c3";
            var stopwatch = Stopwatch.StartNew();

            // Must not throw: the busy_timeout hardening (T012) makes this wait for the
            // holder's COMMIT above instead of surfacing SqliteException(SQLITE_BUSY).
            await repository.UpsertAsync(new OperationalTaskState(taskId, "running", 4242, DateTimeOffset.UtcNow));
            stopwatch.Stop();

            await releaseTask;

            Assert.True(
                stopwatch.Elapsed >= holdDuration - TimeSpan.FromMilliseconds(150),
                $"The write returned after {stopwatch.Elapsed}, well before the {holdDuration} hold released its " +
                "lock — it did not genuinely wait for the other writer, so this test did not exercise busy_timeout.");

            var stored = await repository.GetByStatusAsync("running");
            Assert.Contains(stored, row => row.TaskId == taskId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Realistic-shape complement to the deterministic test above: two independent
    /// <see cref="OperationalStateRepository"/> instances against the same temp-dir
    /// database file — standing in for the Hub process's repository and a CLI
    /// invocation's repository — fire a high volume of genuinely concurrent writes.
    /// Every one must complete without an unhandled <see cref="SqliteException"/>, and
    /// every row must land (no silent data loss), even though many of these writes race
    /// for the same underlying write lock in the same fraction of a second.
    /// </summary>
    [Fact]
    public async Task TwoRepositoryInstances_WritingConcurrentlyAtVolume_NeverThrow_AndPersistEveryRow()
    {
        var root = CreateTempDir("hub-cli-concurrency-volume");
        var dbPath = Path.Combine(root, "operational-state.db");

        try
        {
            var hubRepository = new OperationalStateRepository(dbPath);
            await hubRepository.InitializeAsync();
            var cliRepository = new OperationalStateRepository(dbPath);

            const int writesPerRepository = 50;
            var writes = new List<Task>();
            var expectedTaskIds = new List<string>();

            for (var i = 0; i < writesPerRepository; i++)
            {
                var hubTaskId = $"2026-08-03-hub-writer-{i:D4}";
                var cliTaskId = $"2026-08-03-cli-writer-{i:D4}";
                expectedTaskIds.Add(hubTaskId);
                expectedTaskIds.Add(cliTaskId);

                writes.Add(hubRepository.UpsertAsync(new OperationalTaskState(hubTaskId, "running", 1000 + i, DateTimeOffset.UtcNow)));
                writes.Add(cliRepository.UpsertAsync(new OperationalTaskState(cliTaskId, "running", 2000 + i, DateTimeOffset.UtcNow)));
            }

            // Must not throw (would surface as an AggregateException wrapping
            // SqliteException(SQLITE_BUSY) if the hardening from T012 were missing or
            // insufficient).
            await Task.WhenAll(writes);

            var stored = await hubRepository.GetByStatusAsync("running");
            var storedTaskIds = stored.Select(row => row.TaskId).ToHashSet(StringComparer.Ordinal);

            foreach (var expectedTaskId in expectedTaskIds)
            {
                Assert.Contains(expectedTaskId, storedTaskIds);
            }
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
