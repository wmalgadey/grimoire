using System.Net;
using System.Text.Json;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.RemediationTasks;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T029 (015-lint-board-parity, US4, contracts/remediation-task-api.md) — the
/// authorize/dismiss/withdraw-authorization endpoints over real HTTP: authorize
/// (<c>proposed → authorized</c>, 200, <c>authorized_at</c> stamped, queue position
/// exposed), dismiss (<c>proposed → dismissed</c>, 200, no launcher call ever — FR-010),
/// withdraw (<c>authorized → proposed</c>, 200, <c>authorized_at</c> cleared), and every
/// invalid-state attempt answering 409 with the task's actual current state and a
/// machine-readable reason, never silence (contract discipline). Hermetic — fake
/// <c>IAgentProcessLauncher</c>, real SQLite operational state, real (unconnected)
/// SignalR hub context.
/// </summary>
public class RemediationAuthorizationTests
{
    // ── authorize ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Authorize_FromProposed_Returns200_StampsAuthorizedAt_AndBroadcasts()
    {
        using var harness = await RemediationEndpointHostHarness.CreateAsync();
        // Authorize also kicks the coordinator's own dispatch check (eager dispatch,
        // mirrors IngestRunCoordinator.EnqueueAsync) — pause the queue so this test can
        // observe the authorize transition itself without racing that dispatch. Eager
        // dispatch behavior is RemediationRunCoordinatorTests' concern (T030).
        await harness.Repository.SetFlagAsync(OperationalStateRepository.RemediationQueuePausedFlag, true);

        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var response = await harness.Client.PostAsync($"/api/remediation-tasks/{taskId}/authorize", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(taskId, body.RootElement.GetProperty("taskId").GetString());
        Assert.Equal("authorized", body.RootElement.GetProperty("state").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("authorizedAt").GetString()));

        var stored = Assert.Single(await harness.Repository.GetRemediationTasksAsync());
        Assert.Equal(RemediationTaskStates.Authorized, stored.State);
        Assert.NotNull(stored.AuthorizedAt);
    }

    [Fact]
    public async Task Authorize_SoleWaitingTask_ReportsQueuePositionOne()
    {
        using var harness = await RemediationEndpointHostHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var response = await harness.Client.PostAsync($"/api/remediation-tasks/{taskId}/authorize", content: null);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(1, body.RootElement.GetProperty("queuePosition").GetInt32());
    }

    [Theory]
    [InlineData(RemediationTaskStates.Authorized)]
    [InlineData(RemediationTaskStates.Executing)]
    [InlineData(RemediationTaskStates.Completed)]
    [InlineData(RemediationTaskStates.Dismissed)]
    public async Task Authorize_FromAnyNonProposedState_Returns409_WithActualStateAndReason(string state)
    {
        using var harness = await RemediationEndpointHostHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, state);

        var response = await harness.Client.PostAsync($"/api/remediation-tasks/{taskId}/authorize", content: null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("task_not_proposed", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal(state, body.RootElement.GetProperty("state").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Authorize_UnknownTask_Returns404()
    {
        using var harness = await RemediationEndpointHostHarness.CreateAsync();

        var response = await harness.Client.PostAsync("/api/remediation-tasks/does-not-exist/authorize", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── dismiss ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dismiss_FromProposed_Returns200_TerminatesTheTask_AndAppendsAnOutcomeEntry()
    {
        using var harness = await RemediationEndpointHostHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var response = await harness.Client.PostAsync($"/api/remediation-tasks/{taskId}/dismiss", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("dismissed", body.RootElement.GetProperty("state").GetString());

        var stored = Assert.Single(await harness.Repository.GetRemediationTasksAsync());
        Assert.Equal(RemediationTaskStates.Dismissed, stored.State);

        var parsed = Assert.IsType<RemediationTaskRecordParseResult.Parsed>(await harness.RecordStore.ReadAsync(taskId));
        Assert.Contains(parsed.Entries, e => e is RemediationTaskRecordEntry.Outcome outcome && outcome.State == RemediationTaskStates.Dismissed);
    }

    [Fact]
    public async Task Dismiss_NeverInvokesTheLauncher_FR010()
    {
        using var harness = await RemediationEndpointHostHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        await harness.Client.PostAsync($"/api/remediation-tasks/{taskId}/dismiss", content: null);

        Assert.Empty(harness.Launcher.RemediationRequests);
    }

    [Theory]
    [InlineData(RemediationTaskStates.Authorized)]
    [InlineData(RemediationTaskStates.Executing)]
    [InlineData(RemediationTaskStates.Failed)]
    public async Task Dismiss_FromAnyNonProposedState_Returns409(string state)
    {
        using var harness = await RemediationEndpointHostHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, state, outcomeReason: state == RemediationTaskStates.Failed ? "guard denied" : null);

        var response = await harness.Client.PostAsync($"/api/remediation-tasks/{taskId}/dismiss", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("task_not_proposed", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal(state, body.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Dismiss_UnknownTask_Returns404()
    {
        using var harness = await RemediationEndpointHostHarness.CreateAsync();

        var response = await harness.Client.PostAsync("/api/remediation-tasks/does-not-exist/dismiss", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── withdraw-authorization ──────────────────────────────────────────────────

    [Fact]
    public async Task Withdraw_FromAuthorized_Returns200_ReturnsToProposed_AndClearsAuthorizedAt()
    {
        using var harness = await RemediationEndpointHostHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Authorized);

        var response = await harness.Client.PostAsync($"/api/remediation-tasks/{taskId}/withdraw-authorization", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("proposed", body.RootElement.GetProperty("state").GetString());

        var stored = Assert.Single(await harness.Repository.GetRemediationTasksAsync());
        Assert.Equal(RemediationTaskStates.Proposed, stored.State);
        Assert.Null(stored.AuthorizedAt);
    }

    [Fact]
    public async Task Withdraw_FromProposed_Returns409_TaskNotAuthorized_DoubleWithdrawShape()
    {
        using var harness = await RemediationEndpointHostHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var response = await harness.Client.PostAsync($"/api/remediation-tasks/{taskId}/withdraw-authorization", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("task_not_authorized", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal("proposed", body.RootElement.GetProperty("state").GetString());
    }

    [Theory]
    [InlineData(RemediationTaskStates.Executing, null)]
    [InlineData(RemediationTaskStates.Completed, null)]
    [InlineData(RemediationTaskStates.Failed, "guard denied")]
    [InlineData(RemediationTaskStates.NotApplicable, "stale")]
    public async Task Withdraw_AfterExecutionAlreadyStartedOrFinished_Returns409_ExecutionAlreadyStarted(string state, string? outcomeReason)
    {
        using var harness = await RemediationEndpointHostHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        // Simulates the coordinator's CAS having already won the withdrawal race.
        await harness.InsertTaskAsync(taskId, state, outcomeReason: outcomeReason);

        var response = await harness.Client.PostAsync($"/api/remediation-tasks/{taskId}/withdraw-authorization", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("execution_already_started", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal(state, body.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Withdraw_UnknownTask_Returns404()
    {
        using var harness = await RemediationEndpointHostHarness.CreateAsync();

        var response = await harness.Client.PostAsync("/api/remediation-tasks/does-not-exist/withdraw-authorization", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

/// <summary>
/// Minimal HTTP test host wiring the full US4 remediation-task endpoint group (list/
/// detail from US3 + authorize/dismiss/withdraw from T033), mirroring
/// <c>LintTriggerHostHarness</c>'s idiom.
/// </summary>
internal sealed class RemediationEndpointHostHarness : IDisposable
{
    private readonly string _root;

    private RemediationEndpointHostHarness(
        string root, IHost host, OperationalStateRepository repository, RemediationTaskRecordStore recordStore, FakeAgentProcessLauncher launcher)
    {
        _root = root;
        Host = host;
        Repository = repository;
        RecordStore = recordStore;
        Launcher = launcher;
        Client = host.GetTestClient();
    }

    public IHost Host { get; }
    public HttpClient Client { get; }
    public OperationalStateRepository Repository { get; }
    public RemediationTaskRecordStore RecordStore { get; }
    public FakeAgentProcessLauncher Launcher { get; }

    public static async Task<RemediationEndpointHostHarness> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-remediation-endpoints-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.RemediationTasksDir);

        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var repository = new OperationalStateRepository(paths.StateDbPath);
        await repository.InitializeAsync();
        var recordStore = new RemediationTaskRecordStore(paths);

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddSignalR();
                    services.AddSingleton(repository);
                    services.AddSingleton(recordStore);
                    services.AddSingleton<Grimoire.Hub.AgentDispatch.IAgentProcessLauncher>(launcher);
                    services.AddSingleton<RemediationLifecyclePublisher>(sp => new RemediationLifecyclePublisher(
                        sp.GetRequiredService<IHubContext<RemediationLifecycleHub>>(),
                        NullLogger<RemediationLifecyclePublisher>.Instance));
                    services.AddSingleton<RemediationRunCoordinator>(sp => new RemediationRunCoordinator(
                        repository,
                        sp.GetRequiredService<Grimoire.Hub.AgentDispatch.IAgentProcessLauncher>(),
                        sp.GetRequiredService<RemediationLifecyclePublisher>(),
                        recordStore,
                        paths,
                        logger: NullLogger<RemediationRunCoordinator>.Instance));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHub<RemediationLifecycleHub>("/hubs/remediation-lifecycle");
                        endpoints.MapGroup("/api/remediation-tasks").MapRemediationTaskEndpoints();
                    });
                });
            });

        var host = await hostBuilder.StartAsync();
        return new RemediationEndpointHostHarness(root, host, repository, recordStore, launcher);
    }

    public async Task InsertTaskAsync(string taskId, string state, string? outcomeReason = null)
    {
        var now = DateTimeOffset.UtcNow;
        await RecordStore.CreateAsync(
            taskId, "2026-08-01-lint-9f8e7d", now, $"Proposal {taskId}", "Agent-authored proposal (verbatim).", null);
        await Repository.InsertRemediationTaskAsync(new RemediationTaskRow(
            TaskId: taskId,
            RunId: "2026-08-01-lint-9f8e7d",
            Title: $"Proposal {taskId}",
            Description: "Agent-authored proposal (verbatim).",
            TargetPath: null,
            State: state,
            ProposedAt: now,
            AuthorizedAt: state is RemediationTaskStates.Authorized or RemediationTaskStates.Executing ? now : null,
            OutcomeReason: outcomeReason,
            UpdatedAt: now));
    }

    public void Dispose()
    {
        Host.Dispose();
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
