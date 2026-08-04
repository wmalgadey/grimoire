using System.Diagnostics;
using Grimoire.Hub;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.RemediationTasks;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;

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
                new FindingsReportStore(paths, NullLogger<FindingsReportStore>.Instance),
                paths,
                logger: NullLogger<LintRunCoordinator>.Instance);

            // Coordinator B: an entirely separate instance (own in-process `_slot`,
            // never contended by A) — the same shape a second Hub/CLI process would have.
            var launcherB = new FakeAgentProcessLauncher();
            var coordinatorB = new LintRunCoordinator(
                launcherB,
                new FindingsReportStore(paths, NullLogger<FindingsReportStore>.Instance),
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
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// T037 (018-hub-cli-commands, Phase 7, research.md D8 obligation 2): proves that
    /// disposing the Hub's composition — exactly what <see cref="Grimoire.Hub.Cli.HubCliApp"/>'s
    /// <c>finally</c> block does before every command process exits — flushes telemetry
    /// recorded during the command's own work, instead of dropping it when the process
    /// dies. No new signal identity is under test here (plan.md ## Observability declares
    /// none): <c>remediation-dismiss</c> is chosen because it is the fastest command with
    /// no agent work (FR-010) and its existing signal
    /// (<see cref="RemediationLifecycleLogEvents.LogTaskDismissed"/>'s log-correlation span,
    /// <c>hub.remediation.task_dismissed</c>) already fires unconditionally on every
    /// dismissal — this test asserts that span actually reaches the exporter <b>after</b>
    /// the host is disposed, not merely that it was created.
    ///
    /// Mirrors <see cref="HubRequestTracingTests"/>'s
    /// <c>AddHubTelemetry(tracing => tracing.AddInMemoryExporter(exportedItems))</c>
    /// pattern (ADR-005) — the real production telemetry registration extension point,
    /// which already accepts a <c>configureTracing</c> delegate
    /// (<see cref="Grimoire.Hub.TelemetryExtensions.AddHubTelemetry"/>) — with one
    /// deliberate deviation: <see cref="InMemoryExporterHelperExtensions.AddInMemoryExporter"/>
    /// pairs the in-memory exporter with a <c>SimpleActivityExportProcessor</c>, which
    /// exports synchronously on every <c>Activity.Stop()</c> — so a test built on it alone
    /// would pass whether or not disposal actually flushes anything, proving nothing about
    /// D8. This test instead pairs the same <see cref="InMemoryExporter{T}"/> with an
    /// explicit long-delay <see cref="BatchActivityExportProcessor"/> (a 60s scheduled
    /// delay no test run reaches), so the span is deliberately still queued — not yet
    /// exported — when <see cref="RemediationTaskTransitionService.DismissAsync"/> returns;
    /// only disposing the built <see cref="WebApplication"/> (via <c>TracerProvider.Dispose</c>'s
    /// documented flush-then-shutdown of every registered processor) can make it appear in
    /// <c>exportedItems</c> within this test's lifetime. That is what distinguishes this
    /// test from one that would pass regardless of whether disposal flushes anything.
    ///
    /// This test builds a minimal-but-real host the same way <c>HubRequestTracingTests.BuildHostAsync</c> and
    /// this file's own <c>HubCliRemediationTestHarness</c> (in <c>HubCliCommandTests.cs</c>)
    /// both do — real <see cref="OperationalStateRepository"/>/
    /// <see cref="RemediationTaskRecordStore"/>/<see cref="RemediationRunCoordinator"/>/
    /// <see cref="RemediationTaskTransitionService"/>, real (unconnected) SignalR hub
    /// context — rather than going through <c>HubHostComposition.BuildAsync</c>'s full
    /// ADR-009 path resolution: that resolver hard-validates the presence of real
    /// instruction/secrets/agent-worker files on disk (<c>GrimoirePathResolver.Resolve</c>),
    /// which every other hermetic test in this feature also avoids by constructing
    /// <see cref="ResolvedGrimoirePaths"/> directly
    /// (<see cref="QueryTurnSubmissionApiTests.BuildResolvedPaths"/>) instead of parsing
    /// CLI args through it. The telemetry behavior under test — does disposing a host that
    /// called <c>AddHubTelemetry</c> flush its recorded spans — is exercised identically
    /// either way, since it lives entirely inside the OpenTelemetry SDK's
    /// <c>TracerProvider</c>, not in <c>HubHostComposition</c> itself.
    /// </summary>
    [Fact]
    public async Task HostDisposal_FlushesTelemetry_ForACliInvokedFlow_BeforeProcessExit()
    {
        var root = CreateTempDir("hub-cli-telemetry-flush");
        var exportedItems = new List<Activity>();

        try
        {
            var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
            Directory.CreateDirectory(paths.RemediationTasksDir);

            var hostBuilder = WebApplication.CreateBuilder();
            hostBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            hostBuilder.Services.AddSignalR();
            // The exact production registration call (ADR-005) — configureTracing is the
            // seam that already exists at this level. A deliberately long scheduled delay
            // (see the class doc above) keeps the span queued, not yet exported, until
            // disposal forces the flush this test exists to prove.
            hostBuilder.Services.AddHubTelemetry(tracing => tracing.AddProcessor(
                new BatchActivityExportProcessor(
                    new InMemoryExporter<Activity>(exportedItems),
                    maxQueueSize: 2048,
                    scheduledDelayMilliseconds: 60_000,
                    exporterTimeoutMilliseconds: 30_000,
                    maxExportBatchSize: 512)));

            RemediationTaskTransitionService transitionService;
            string taskId;
            await using (var host = hostBuilder.Build())
            {
                host.MapHub<RemediationLifecycleHub>("/hubs/remediation-lifecycle");
                await host.StartAsync();

                var repository = new OperationalStateRepository(paths.StateDbPath);
                await repository.InitializeAsync();
                var recordStore = new RemediationTaskRecordStore(paths);
                var publisher = new RemediationLifecyclePublisher(
                    host.Services.GetRequiredService<IHubContext<RemediationLifecycleHub>>(),
                    NullLogger<RemediationLifecyclePublisher>.Instance);
                var coordinator = new RemediationRunCoordinator(
                    repository, new FakeAgentProcessLauncher(), publisher, recordStore, paths,
                    logger: NullLogger<RemediationRunCoordinator>.Instance);
                transitionService = new RemediationTaskTransitionService(
                    repository, publisher, coordinator, recordStore, NullLogger<RemediationLifecyclePublisher>.Instance);

                taskId = "2026-08-04-telemetry-flush1";
                var now = DateTimeOffset.UtcNow;
                await recordStore.CreateAsync(taskId, "2026-08-04-lint-run01", now, "Proposal", "Agent-authored proposal (verbatim).", null);
                await repository.InsertRemediationTaskAsync(new RemediationTaskRow(
                    TaskId: taskId, RunId: "2026-08-04-lint-run01", Title: "Proposal", Description: "Agent-authored proposal (verbatim).",
                    TargetPath: null, State: RemediationTaskStates.Proposed, ProposedAt: now, AuthorizedAt: null,
                    OutcomeReason: null, UpdatedAt: now));

                // The CLI-invoked flow itself: RemediationDismissCommand's own ExecuteAsync
                // calls exactly this.
                var result = await transitionService.DismissAsync(taskId);
                Assert.IsType<RemediationTransitionResult.Ok>(result);

                // The batch processor's 60s scheduled delay guarantees the span is still
                // queued, not yet exported, at this point — proving the assertion after
                // disposal below is genuinely exercising the flush, not a no-op check.
                lock (exportedItems)
                {
                    Assert.Empty(exportedItems);
                }
            }
            // `await using` above has now disposed the host — the same call
            // Grimoire.Hub.Cli.HubCliApp.RunAsync's `finally` block makes before every
            // command process exits (research.md D8 obligation 1).

            var flushed = await WaitForSpanAsync(exportedItems, "hub.remediation.task_dismissed", TimeSpan.FromSeconds(5));
            Assert.True(flushed.Recorded);
            var taskIdTag = flushed.TagObjects.FirstOrDefault(t => t.Key == "task_id").Value?.ToString();
            Assert.Equal(taskId, taskIdTag);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<Activity> WaitForSpanAsync(List<Activity> exportedItems, string operationName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Activity? match;
            lock (exportedItems)
            {
                match = exportedItems.FirstOrDefault(a => a.OperationName == operationName);
            }

            if (match is not null)
            {
                return match;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Span '{operationName}' was never exported, even after host disposal.");
    }

    private static string CreateTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
