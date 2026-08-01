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
/// T031 (015-lint-board-parity, US4, ADR-003/ADR-018, data-model.md "Restart
/// reconciliation") — <c>RestartReconciler.ReconcileRemediationTasksAsync</c> and
/// <c>RemediationRunCoordinator.InitializeAsync</c> together: an <c>Executing</c> row
/// with no live process is failed with a reason on startup (mirroring the existing
/// ingest reconciliation rule); <c>Proposed</c> and terminal rows are left untouched;
/// <c>Authorized</c> rows survive the restart still authorized, but the remediation
/// execution queue starts paused on its own flag — independent of ingest's
/// <c>queue_paused</c> — until explicitly resumed. Hermetic — real SQLite operational
/// state, real (unconnected) SignalR hub context, no live agent process.
/// </summary>
public class RemediationRestartReconcilerTests
{
    [Fact]
    public async Task ExecutingRow_WithNoLiveProcess_IsFailedOnStartup_WithReason_AndOutcomeEntryAppended()
    {
        var fixture = await ReconcilerFixture.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await fixture.InsertTaskAsync(taskId, RemediationTaskStates.Executing);

        var reconciler = new RestartReconciler(fixture.Repository, NullLogger<RestartReconciler>.Instance);
        var reconciledCount = await reconciler.ReconcileRemediationTasksAsync(fixture.RecordStore);

        Assert.Equal(1, reconciledCount);

        var stored = Assert.Single(await fixture.Repository.GetRemediationTasksAsync());
        Assert.Equal(RemediationTaskStates.Failed, stored.State);
        Assert.NotNull(stored.OutcomeReason);
        Assert.Contains("restart", stored.OutcomeReason, StringComparison.OrdinalIgnoreCase);

        var parsed = Assert.IsType<RemediationTaskRecordParseResult.Parsed>(await fixture.RecordStore.ReadAsync(taskId));
        var outcome = Assert.Single(parsed.Entries.OfType<RemediationTaskRecordEntry.Outcome>());
        Assert.Equal(RemediationTaskStates.Failed, outcome.State);
        Assert.Equal(stored.OutcomeReason, outcome.Reason);
    }

    [Fact]
    public async Task MultipleExecutingRows_AreAllFailed()
    {
        var fixture = await ReconcilerFixture.CreateAsync();
        await fixture.InsertTaskAsync("2026-08-01-remediation-aaaaaa", RemediationTaskStates.Executing);
        await fixture.InsertTaskAsync("2026-08-01-remediation-bbbbbb", RemediationTaskStates.Executing);

        var reconciler = new RestartReconciler(fixture.Repository, NullLogger<RestartReconciler>.Instance);
        var reconciledCount = await reconciler.ReconcileRemediationTasksAsync(fixture.RecordStore);

        Assert.Equal(2, reconciledCount);
        var rows = await fixture.Repository.GetRemediationTasksAsync();
        Assert.All(rows, r => Assert.Equal(RemediationTaskStates.Failed, r.State));
    }

    [Theory]
    [InlineData(RemediationTaskStates.Proposed)]
    [InlineData(RemediationTaskStates.Completed)]
    [InlineData(RemediationTaskStates.Dismissed)]
    public async Task ProposedAndTerminalRows_AreLeftUntouched(string state)
    {
        var fixture = await ReconcilerFixture.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await fixture.InsertTaskAsync(taskId, state);

        var reconciler = new RestartReconciler(fixture.Repository, NullLogger<RestartReconciler>.Instance);
        var reconciledCount = await reconciler.ReconcileRemediationTasksAsync(fixture.RecordStore);

        Assert.Equal(0, reconciledCount);
        var stored = Assert.Single(await fixture.Repository.GetRemediationTasksAsync());
        Assert.Equal(state, stored.State);
    }

    [Fact]
    public async Task AuthorizedRow_SurvivesReconciliation_StillAuthorized()
    {
        var fixture = await ReconcilerFixture.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await fixture.InsertTaskAsync(taskId, RemediationTaskStates.Authorized, authorizedAt: DateTimeOffset.UtcNow);

        var reconciler = new RestartReconciler(fixture.Repository, NullLogger<RestartReconciler>.Instance);
        var reconciledCount = await reconciler.ReconcileRemediationTasksAsync(fixture.RecordStore);

        Assert.Equal(0, reconciledCount);
        var stored = Assert.Single(await fixture.Repository.GetRemediationTasksAsync());
        Assert.Equal(RemediationTaskStates.Authorized, stored.State);
        Assert.NotNull(stored.AuthorizedAt);
    }

    [Fact]
    public async Task SurvivingAuthorizedRows_PauseTheRemediationQueue_OnCoordinatorInitialize()
    {
        var fixture = await ReconcilerFixture.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await fixture.InsertTaskAsync(taskId, RemediationTaskStates.Authorized, authorizedAt: DateTimeOffset.UtcNow);

        // Reconciliation itself doesn't touch Authorized rows or the pause flag — that's
        // the coordinator's own startup rule, mirroring IngestRunCoordinator.InitializeAsync.
        var reconciler = new RestartReconciler(fixture.Repository, NullLogger<RestartReconciler>.Instance);
        await reconciler.ReconcileRemediationTasksAsync(fixture.RecordStore);

        await using var app = await StartHubHostAsync();
        var coordinator = fixture.CreateCoordinator(app, new FakeAgentProcessLauncher(autoPlay: false));

        await coordinator.InitializeAsync();

        Assert.True(await coordinator.IsQueuePausedAsync());

        // The paused queue must not auto-dispatch the surviving Authorized row.
        await coordinator.TryStartNextAsync();
        var launcher = Assert.IsType<FakeAgentProcessLauncher>(fixture.LastLauncher);
        Assert.Empty(launcher.RemediationRequests);
    }

    [Fact]
    public async Task NoExecutingOrAuthorizedRows_QueueStartsUnpaused()
    {
        var fixture = await ReconcilerFixture.CreateAsync();
        await fixture.InsertTaskAsync("2026-08-01-remediation-a1b2c3", RemediationTaskStates.Proposed);

        await using var app = await StartHubHostAsync();
        var coordinator = fixture.CreateCoordinator(app, new FakeAgentProcessLauncher(autoPlay: false));

        await coordinator.InitializeAsync();

        Assert.False(await coordinator.IsQueuePausedAsync());
    }

    [Fact]
    public async Task RemediationQueuePausedFlag_StaysIndependentOfIngestsQueuePausedFlag()
    {
        var fixture = await ReconcilerFixture.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await fixture.InsertTaskAsync(taskId, RemediationTaskStates.Authorized, authorizedAt: DateTimeOffset.UtcNow);

        // Ingest's own flag key, independent per T004's design (RemediationTaskRepositoryTests
        // asserts the value directly rather than referencing IngestRunCoordinator, ADR-013 N1).
        const string ingestQueuePausedFlag = "queue_paused";
        await fixture.Repository.SetFlagAsync(ingestQueuePausedFlag, false);

        await using var app = await StartHubHostAsync();
        var coordinator = fixture.CreateCoordinator(app, new FakeAgentProcessLauncher(autoPlay: false));
        await coordinator.InitializeAsync();

        Assert.True(await coordinator.IsQueuePausedAsync());
        Assert.False(await fixture.Repository.GetFlagAsync(ingestQueuePausedFlag));
    }

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

/// <summary>Shared temp-root SQLite + record store fixture for the reconciler tests above.</summary>
internal sealed class ReconcilerFixture
{
    private readonly string _root;

    private ReconcilerFixture(string root, OperationalStateRepository repository, RemediationTaskRecordStore recordStore, ResolvedGrimoirePaths paths)
    {
        _root = root;
        Repository = repository;
        RecordStore = recordStore;
        Paths = paths;
    }

    public OperationalStateRepository Repository { get; }
    public RemediationTaskRecordStore RecordStore { get; }
    public ResolvedGrimoirePaths Paths { get; }

    /// <summary>Exposes the launcher passed to <see cref="CreateCoordinator"/>, so tests can assert on it without threading it through separately.</summary>
    public FakeAgentProcessLauncher? LastLauncher { get; private set; }

    public static async Task<ReconcilerFixture> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-remediation-reconciler-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.RemediationTasksDir);

        var repository = new OperationalStateRepository(paths.StateDbPath);
        await repository.InitializeAsync();
        var recordStore = new RemediationTaskRecordStore(paths);

        return new ReconcilerFixture(root, repository, recordStore, paths);
    }

    public async Task InsertTaskAsync(string taskId, string state, DateTimeOffset? authorizedAt = null, string? outcomeReason = null)
    {
        var now = DateTimeOffset.UtcNow;
        await Repository.InsertRemediationTaskAsync(new RemediationTaskRow(
            TaskId: taskId,
            RunId: "2026-08-01-lint-9f8e7d",
            Title: $"Proposal {taskId}",
            Description: "Agent-authored proposal (verbatim).",
            TargetPath: null,
            State: state,
            ProposedAt: now,
            AuthorizedAt: authorizedAt,
            OutcomeReason: outcomeReason,
            UpdatedAt: now));
        await RecordStore.CreateAsync(
            taskId, "2026-08-01-lint-9f8e7d", now, $"Proposal {taskId}", "Agent-authored proposal (verbatim).", null);
    }

    public RemediationRunCoordinator CreateCoordinator(WebApplication hubHost, FakeAgentProcessLauncher launcher)
    {
        LastLauncher = launcher;
        var publisher = new RemediationLifecyclePublisher(hubHost.Services.GetRequiredService<IHubContext<RemediationLifecycleHub>>());
        return new RemediationRunCoordinator(
            Repository, launcher, publisher, RecordStore, Paths, logger: NullLogger<RemediationRunCoordinator>.Instance);
    }
}
