using Grimoire.Hub.OperationalState;
using Grimoire.Hub.RemediationTasks;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T030 (015-lint-board-parity, US4, ADR-018) — <c>RemediationRunCoordinator</c>: FIFO
/// dispatch order by <c>authorized_at</c> (FR-017); exactly one <c>Executing</c> task at a
/// time; queue positions exposed for the tasks still waiting; the queue auto-advances on
/// every terminal transition (completed, failed, liveness expiry, spawn failure);
/// <c>completed</c> + <c>remediationOutcome: not_applicable</c> transports to
/// <c>NotApplicable</c> with its reason (FR-018); and — the central SC-005 proof — the
/// fake <c>IAgentProcessLauncher</c> is never invoked for any row that is not
/// <c>Authorized</c>, and the withdrawal-vs-dispatch race resolves to exactly one winner.
/// Hermetic — fake agent launcher, real SQLite operational state, real (unconnected)
/// SignalR hub context for the lifecycle publisher.
/// </summary>
public class RemediationRunCoordinatorTests
{
    // ── FIFO order + auto-advance ──────────────────────────────────────────────────

    [Fact]
    public async Task Dispatch_PicksTheOldestAuthorizedTask_FIFO_ByAuthorizedAt()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, autoPlay: false);

        var t0 = DateTimeOffset.UtcNow;
        await harness.InsertAuthorizedAsync("2026-08-01-remediation-second", t0.AddSeconds(2));
        await harness.InsertAuthorizedAsync("2026-08-01-remediation-first", t0);
        await harness.InsertAuthorizedAsync("2026-08-01-remediation-third", t0.AddSeconds(4));

        await harness.Coordinator.TryStartNextAsync();

        var request = Assert.Single(harness.Launcher.RemediationRequests);
        Assert.Equal("2026-08-01-remediation-first", request.TaskId);

        var executing = (await harness.Repository.GetRemediationTasksAsync()).Single(r => r.TaskId == request.TaskId);
        Assert.Equal("executing", executing.State);
    }

    [Fact]
    public async Task QueueAdvances_AfterACompletedTerminalEvent_ToTheNextOldestAuthorizedTask()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, autoPlay: false);

        var t0 = DateTimeOffset.UtcNow;
        await harness.InsertAuthorizedAsync("2026-08-01-remediation-first", t0);
        await harness.InsertAuthorizedAsync("2026-08-01-remediation-second", t0.AddSeconds(2));

        await harness.Coordinator.TryStartNextAsync();
        var first = Assert.Single(harness.Launcher.RemediationRequests);
        Assert.Equal("2026-08-01-remediation-first", first.TaskId);

        var handle = Assert.Single(harness.Launcher.Handles);
        handle.EmitEvent("completed", first.TaskId, new { summary = "Applied the fix." });

        await harness.WaitForStateAsync(first.TaskId, RemediationTaskStates.Completed);
        await harness.WaitForRequestCountAsync(2);

        Assert.Equal("2026-08-01-remediation-second", harness.Launcher.RemediationRequests[1].TaskId);
        var second = (await harness.Repository.GetRemediationTasksAsync())
            .Single(r => r.TaskId == "2026-08-01-remediation-second");
        Assert.Equal("executing", second.State);
    }

    [Fact]
    public async Task QueueAdvances_AfterAFailedTerminalEvent_NotOnlyAfterCompleted()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, autoPlay: false);

        var t0 = DateTimeOffset.UtcNow;
        await harness.InsertAuthorizedAsync("2026-08-01-remediation-first", t0);
        await harness.InsertAuthorizedAsync("2026-08-01-remediation-second", t0.AddSeconds(2));

        await harness.Coordinator.TryStartNextAsync();
        var first = Assert.Single(harness.Launcher.RemediationRequests);

        var handle = Assert.Single(harness.Launcher.Handles);
        handle.EmitEvent("failed", first.TaskId, new { reason = "Guard denied: change exceeds frontmatter-only scope." });

        var failedRow = await harness.WaitForStateAsync(first.TaskId, RemediationTaskStates.Failed);
        Assert.Contains("Guard denied", failedRow.OutcomeReason);

        await harness.WaitForRequestCountAsync(2);
        Assert.Equal("2026-08-01-remediation-second", harness.Launcher.RemediationRequests[1].TaskId);
    }

    // ── exactly one Executing at a time ────────────────────────────────────────────

    [Fact]
    public async Task OnlyOneTaskExecutesAtATime_ConcurrentDispatchAttempts_StartExactlyOne()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, autoPlay: false);

        var t0 = DateTimeOffset.UtcNow;
        await harness.InsertAuthorizedAsync("2026-08-01-remediation-a", t0);
        await harness.InsertAuthorizedAsync("2026-08-01-remediation-b", t0.AddSeconds(1));

        await Task.WhenAll(
            harness.Coordinator.TryStartNextAsync(),
            harness.Coordinator.TryStartNextAsync(),
            harness.Coordinator.TryStartNextAsync());

        Assert.Single(harness.Launcher.RemediationRequests);
        var rows = await harness.Repository.GetRemediationTasksAsync();
        Assert.Single(rows, r => r.State == "executing");
        Assert.Single(rows, r => r.State == "authorized");
    }

    // ── queue positions ─────────────────────────────────────────────────────────

    [Fact]
    public async Task QueuePositions_ExposeWaitingOrder_ExcludingTheExecutingTask()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, autoPlay: false);

        var t0 = DateTimeOffset.UtcNow;
        await harness.InsertAuthorizedAsync("2026-08-01-remediation-a", t0);
        await harness.InsertAuthorizedAsync("2026-08-01-remediation-b", t0.AddSeconds(1));
        await harness.InsertAuthorizedAsync("2026-08-01-remediation-c", t0.AddSeconds(2));

        await harness.Coordinator.TryStartNextAsync();

        var positions = await harness.Coordinator.GetQueuePositionsAsync();
        Assert.False(positions.ContainsKey("2026-08-01-remediation-a"), "The executing task must not carry a waiting queue position.");
        Assert.Equal(1, positions["2026-08-01-remediation-b"]);
        Assert.Equal(2, positions["2026-08-01-remediation-c"]);
    }

    // ── liveness expiry / spawn failure → Failed + reason ──────────────────────────

    [Fact]
    public async Task LivenessWindowExpiry_MarksTheExecutingTaskFailed_WithReason_AndTerminatesTheProcess()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(
            app, autoPlay: false, livenessWindow: TimeSpan.FromMilliseconds(100));

        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertAuthorizedAsync(taskId, DateTimeOffset.UtcNow);

        await harness.Coordinator.TryStartNextAsync();
        var handle = Assert.Single(harness.Launcher.Handles);
        handle.EmitEvent("started", taskId);
        // ... then silence: no heartbeat, no activity, no terminal event at all.

        var row = await harness.WaitForStateAsync(taskId, RemediationTaskStates.Failed);
        Assert.Contains("liveness", row.OutcomeReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(handle.Terminated, "The leftover agent process must be terminated on liveness failure.");
    }

    [Fact]
    public async Task SpawnFailure_MarksTheTaskFailed_WithTheExceptionReason()
    {
        await using var app = await StartHubHostAsync();
        var throwingLauncher = new FakeAgentProcessLauncher(throwOnStart: new InvalidOperationException("boom"), autoPlay: false);
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, launcher: throwingLauncher);

        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertAuthorizedAsync(taskId, DateTimeOffset.UtcNow);

        await harness.Coordinator.TryStartNextAsync();

        var row = await harness.WaitForStateAsync(taskId, RemediationTaskStates.Failed);
        Assert.Contains("could not be started", row.OutcomeReason);
        Assert.Contains("boom", row.OutcomeReason);
    }

    // ── remediationOutcome transport (FR-018) ──────────────────────────────────────

    [Fact]
    public async Task CompletedEventCarryingNotApplicableOutcome_TransitionsToNotApplicable_WithTheAgentsReason()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, autoPlay: false);

        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertAuthorizedAsync(taskId, DateTimeOffset.UtcNow);

        await harness.Coordinator.TryStartNextAsync();
        var handle = Assert.Single(harness.Launcher.Handles);
        handle.EmitEventWithFields("completed", taskId, new Dictionary<string, object?>
        {
            ["remediationOutcome"] = "not_applicable",
            ["reason"] = "Tags already present; proposal is moot.",
        });

        var row = await harness.WaitForStateAsync(taskId, RemediationTaskStates.NotApplicable);
        Assert.Equal("Tags already present; proposal is moot.", row.OutcomeReason);
    }

    [Fact]
    public async Task CompletedEventWithoutRemediationOutcome_TransitionsToCompleted_WithNoReason()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, autoPlay: false);

        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertAuthorizedAsync(taskId, DateTimeOffset.UtcNow);

        await harness.Coordinator.TryStartNextAsync();
        var handle = Assert.Single(harness.Launcher.Handles);
        handle.EmitEvent("completed", taskId, new { summary = "Applied the fix." });

        var row = await harness.WaitForStateAsync(taskId, RemediationTaskStates.Completed);
        Assert.Null(row.OutcomeReason);
    }

    // ── SC-005: the launcher is never invoked for any non-Authorized row ───────────

    [Fact]
    public async Task Launcher_IsNeverInvoked_ForAnyNonAuthorizedRow_SC005()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, autoPlay: false);

        var now = DateTimeOffset.UtcNow;
        // One row per state the coordinator must never dispatch from — Authorized is the
        // sole state absent here on purpose (data-model.md "Validation rules").
        await harness.InsertTaskAsync("2026-08-01-remediation-proposed", RemediationTaskStates.Proposed, now);
        await harness.InsertTaskAsync("2026-08-01-remediation-executing", RemediationTaskStates.Executing, now);
        await harness.InsertTaskAsync("2026-08-01-remediation-completed", RemediationTaskStates.Completed, now);
        await harness.InsertTaskAsync("2026-08-01-remediation-failed", RemediationTaskStates.Failed, now, outcomeReason: "guard denied");
        await harness.InsertTaskAsync("2026-08-01-remediation-notapplic", RemediationTaskStates.NotApplicable, now, outcomeReason: "stale");
        await harness.InsertTaskAsync("2026-08-01-remediation-dismissed", RemediationTaskStates.Dismissed, now);

        // Drive dispatch repeatedly — SC-005 must hold under repeated pressure, not just once.
        await harness.Coordinator.TryStartNextAsync();
        await harness.Coordinator.TryStartNextAsync();
        await harness.Coordinator.TryStartNextAsync();
        await Task.Delay(50); // give any errant background dispatch a chance to surface

        Assert.Empty(harness.Launcher.RemediationRequests);
        Assert.Null(harness.Coordinator.RunningTaskId);

        // The rows themselves are untouched — dispatch is a structural no-op here, not a
        // check that happens to reject after the fact.
        var rows = await harness.Repository.GetRemediationTasksAsync();
        Assert.Equal(6, rows.Count);
        Assert.DoesNotContain(rows, r => r.State == RemediationTaskStates.Executing && r.TaskId != "2026-08-01-remediation-executing");
    }

    // ── withdrawal race (FR-016, spec Edge Cases) ──────────────────────────────────

    [Fact]
    public async Task WithdrawalRace_AgainstDispatch_ResolvesToExactlyOneWinner_LauncherCalledAtMostOnce()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, autoPlay: false);

        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertAuthorizedAsync(taskId, DateTimeOffset.UtcNow);

        // Race: the coordinator's Authorized→Executing CAS (inside the slot lock, before
        // spawn) vs. a withdrawal's Authorized→Proposed CAS on the same persisted row —
        // the SQLite CAS is the single deterministic arbiter (ADR-018/research.md R5).
        var dispatch = harness.Coordinator.TryStartNextAsync();
        var withdraw = harness.Repository.TryTransitionRemediationTaskAsync(
            taskId, RemediationTaskStates.Authorized, RemediationTaskStates.Proposed,
            outcomeReason: null, authorizedAt: null, updatedAt: DateTimeOffset.UtcNow);

        await Task.WhenAll(dispatch, withdraw);

        var row = Assert.Single(await harness.Repository.GetRemediationTasksAsync());
        if (row.State == RemediationTaskStates.Proposed)
        {
            // Withdrawal won: no execution was ever dispatched (SC-005 holds even mid-race).
            Assert.Empty(harness.Launcher.RemediationRequests);
        }
        else
        {
            // Dispatch won: it committed Authorized → Executing before the withdrawal's
            // CAS could apply, and exactly one process was spawned.
            Assert.Equal(RemediationTaskStates.Executing, row.State);
            Assert.Single(harness.Launcher.RemediationRequests);
        }
    }

    // ── shared setup ────────────────────────────────────────────────────────────

    private static async Task<WebApplication> StartHubHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        var app = builder.Build();
        app.MapHub<RemediationLifecycleHub>("/hubs/remediation-lifecycle");
        await app.StartAsync();
        return app;
    }
}

/// <summary>
/// <see cref="RemediationRunCoordinator"/> harness with real SQLite operational state, a
/// real (unconnected) SignalR hub context for the lifecycle publisher, and a scriptable
/// <see cref="FakeAgentProcessLauncher"/> — mirrors <c>LintCoordinatorHarness</c>'s idiom.
/// </summary>
internal sealed class RemediationCoordinatorHarness : IAsyncDisposable
{
    private readonly string _root;

    private RemediationCoordinatorHarness(
        string root,
        ResolvedGrimoirePaths paths,
        FakeAgentProcessLauncher launcher,
        OperationalStateRepository repository,
        RemediationTaskRecordStore recordStore,
        RemediationRunCoordinator coordinator)
    {
        _root = root;
        Paths = paths;
        Launcher = launcher;
        Repository = repository;
        RecordStore = recordStore;
        Coordinator = coordinator;
    }

    public ResolvedGrimoirePaths Paths { get; }
    public FakeAgentProcessLauncher Launcher { get; }
    public OperationalStateRepository Repository { get; }
    public RemediationTaskRecordStore RecordStore { get; }
    public RemediationRunCoordinator Coordinator { get; }

    public static async Task<RemediationCoordinatorHarness> CreateAsync(
        WebApplication hubHost, FakeAgentProcessLauncher? launcher = null, bool autoPlay = true, TimeSpan? livenessWindow = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-remediation-coordinator-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.RemediationTasksDir);

        var effectiveLauncher = launcher ?? new FakeAgentProcessLauncher(autoPlay: autoPlay);
        var repository = new OperationalStateRepository(paths.StateDbPath);
        await repository.InitializeAsync();
        var recordStore = new RemediationTaskRecordStore(paths);

        var publisher = new RemediationLifecyclePublisher(
            hubHost.Services.GetRequiredService<IHubContext<RemediationLifecycleHub>>());

        var coordinator = new RemediationRunCoordinator(
            repository, effectiveLauncher, publisher, recordStore, paths,
            livenessWindow: livenessWindow, logger: NullLogger<RemediationRunCoordinator>.Instance);

        return new RemediationCoordinatorHarness(root, paths, effectiveLauncher, repository, recordStore, coordinator);
    }

    public Task InsertAuthorizedAsync(string taskId, DateTimeOffset authorizedAt)
        => InsertTaskAsync(taskId, RemediationTaskStates.Authorized, authorizedAt, authorizedAt: authorizedAt);

    public async Task InsertTaskAsync(
        string taskId, string state, DateTimeOffset proposedAt, DateTimeOffset? authorizedAt = null, string? outcomeReason = null)
    {
        await Repository.InsertRemediationTaskAsync(new RemediationTaskRow(
            TaskId: taskId,
            RunId: "2026-08-01-lint-9f8e7d",
            Title: $"Proposal {taskId}",
            Description: "Agent-authored proposal (verbatim).",
            TargetPath: null,
            State: state,
            ProposedAt: proposedAt,
            AuthorizedAt: authorizedAt,
            OutcomeReason: outcomeReason,
            UpdatedAt: proposedAt));
        await RecordStore.CreateAsync(
            taskId, "2026-08-01-lint-9f8e7d", proposedAt, $"Proposal {taskId}", "Agent-authored proposal (verbatim).", null);
    }

    public async Task<RemediationTaskRow> WaitForStateAsync(string taskId, string state, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        RemediationTaskRow? row = null;
        while (DateTime.UtcNow < deadline)
        {
            row = (await Repository.GetRemediationTasksAsync()).SingleOrDefault(r => r.TaskId == taskId);
            if (row is not null && row.State == state)
            {
                return row;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Task '{taskId}' did not reach state '{state}' in time (last seen: {row?.State ?? "not found"}).");
        return null!;
    }

    public async Task WaitForRequestCountAsync(int count, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (Launcher.RemediationRequests.Count >= count)
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Expected at least {count} remediation execution requests, saw {Launcher.RemediationRequests.Count}.");
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        }

        return ValueTask.CompletedTask;
    }
}
