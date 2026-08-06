using System.Net;
using System.Text.Json;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.OperationalState;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T016 (015-lint-board-parity, US2, FR-004/SC-004) — `POST /api/lint-runs` trigger
/// preconditions per contracts/lint-board-api.md: 409 `lint_run_active` while a run is
/// active; 409 `unresolved_remediation_tasks` + `unresolvedTaskIds` while any remediation
/// row is `proposed|authorized|executing`; 202 when neither holds — including the
/// run-just-finished race, which always resolves to a clean accept or a clean reject
/// naming the reason, never silence (spec Edge Cases). Hermetic — fake agent launcher,
/// temp SQLite operational state.
/// </summary>
public class LintTriggerPreconditionTests
{
    [Fact]
    public async Task Post_WhileARunIsActive_Returns409_WithLintRunActiveReason()
    {
        using var harness = await LintTriggerHostHarness.CreateAsync(
            new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromSeconds(10)));
        var client = harness.Host.GetTestClient();

        var first = await client.PostAsync("/api/lint-runs/", content: null);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var second = await client.PostAsync("/api/lint-runs/", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("lint_run_active", body.RootElement.GetProperty("reason").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Post_WithUnresolvedRemediationTasks_Returns409_NamingTheBlockingTaskIds()
    {
        using var harness = await LintTriggerHostHarness.CreateAsync();

        // Unresolved = proposed | authorized | executing (data-model.md); terminal rows
        // must not block.
        await harness.InsertRemediationTaskAsync("2026-08-01-remediation-proposed1", "proposed");
        await harness.InsertRemediationTaskAsync("2026-08-01-remediation-authrzd1", "authorized");
        await harness.InsertRemediationTaskAsync("2026-08-01-remediation-executng", "executing");
        await harness.InsertRemediationTaskAsync("2026-08-01-remediation-complete", "completed");

        var client = harness.Host.GetTestClient();
        var response = await client.PostAsync("/api/lint-runs/", content: null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unresolved_remediation_tasks", body.RootElement.GetProperty("reason").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("message").GetString()));

        var unresolvedIds = body.RootElement.GetProperty("unresolvedTaskIds")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(
            ["2026-08-01-remediation-authrzd1", "2026-08-01-remediation-executng", "2026-08-01-remediation-proposed1"],
            unresolvedIds.Order());

        // Blocked means blocked: no agent process was ever spawned (FR-004).
        Assert.Empty(harness.Launcher.LintRequests);
    }

    [Fact]
    public async Task Post_WithOnlyTerminalRemediationTasks_IsAccepted()
    {
        using var harness = await LintTriggerHostHarness.CreateAsync();

        await harness.InsertRemediationTaskAsync("2026-08-01-remediation-complete", "completed");
        await harness.InsertRemediationTaskAsync("2026-08-01-remediation-failed01", "failed", outcomeReason: "guard denied");
        await harness.InsertRemediationTaskAsync("2026-08-01-remediation-notappli", "not_applicable", outcomeReason: "stale");
        await harness.InsertRemediationTaskAsync("2026-08-01-remediation-dismissd", "dismissed");

        var client = harness.Host.GetTestClient();
        var response = await client.PostAsync("/api/lint-runs/", content: null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("runId").GetString()));
        Assert.Equal("running", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Post_AroundRunCompletion_AlwaysResolvesToDefiniteAcceptOrReject_NeverSilence()
    {
        // Spec edge case: a trigger at the exact moment a run finishes must leave the
        // caller certain of the outcome — every response is either a definitive 202
        // (new run exists) or a definitive 409 naming the reason.
        using var harness = await LintTriggerHostHarness.CreateAsync(
            new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromMilliseconds(50)));
        var client = harness.Host.GetTestClient();

        var first = await client.PostAsync("/api/lint-runs/", content: null);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var sawAccepted = false;
        await PollAsync.WaitAsync(
            async () =>
            {
                var response = await client.PostAsync("/api/lint-runs/", content: null);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                if (response.StatusCode == HttpStatusCode.Accepted)
                {
                    // Clean accept: a new run definitively exists.
                    Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("runId").GetString()));
                    sawAccepted = true;
                    return true;
                }

                // Clean reject: 409 naming the reason — never any other shape.
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal("lint_run_active", body.RootElement.GetProperty("reason").GetString());
                return false;
            },
            TimeSpan.FromSeconds(10),
            "Retrying after the previous run finished must eventually yield a clean 202.",
            pollInterval: TimeSpan.FromMilliseconds(10));

        Assert.True(sawAccepted, "Retrying after the previous run finished must eventually yield a clean 202.");
    }
}

/// <summary>
/// TestServer host wiring the lint trigger endpoint against a
/// <see cref="LintRunCoordinator"/> that sees the real SQLite operational state
/// (remediation rows) — mirrors <see cref="BoardHostHarness"/>' idiom.
/// </summary>
internal sealed class LintTriggerHostHarness : IDisposable
{
    private readonly string _root;

    private LintTriggerHostHarness(
        string root, IHost host, FakeAgentProcessLauncher launcher, OperationalStateRepository repository)
    {
        _root = root;
        Host = host;
        Launcher = launcher;
        Repository = repository;
    }

    public IHost Host { get; }
    public FakeAgentProcessLauncher Launcher { get; }
    public OperationalStateRepository Repository { get; }

    public static async Task<LintTriggerHostHarness> CreateAsync(FakeAgentProcessLauncher? launcher = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-lint-precondition-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.FindingsDir);

        var effectiveLauncher = launcher ?? new FakeAgentProcessLauncher(autoPlay: true);
        var repository = new OperationalStateRepository(paths.StateDbPath);
        await repository.InitializeAsync();

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddSingleton(repository);
                    services.AddSingleton<FindingsReportStore>(sp =>
                        new FindingsReportStore(paths, NullLogger<FindingsReportStore>.Instance));
                    services.AddSingleton<LintRunCoordinator>(sp => new LintRunCoordinator(
                        effectiveLauncher,
                        sp.GetRequiredService<FindingsReportStore>(),
                        paths,
                        logger: NullLogger<LintRunCoordinator>.Instance,
                        stateRepository: repository));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGroup("/api/lint-runs").MapLintRunEndpoints();
                    });
                });
            });

        var host = await hostBuilder.StartAsync();
        return new LintTriggerHostHarness(root, host, effectiveLauncher, repository);
    }

    public Task InsertRemediationTaskAsync(string taskId, string state, string? outcomeReason = null)
    {
        var now = DateTimeOffset.UtcNow;
        return Repository.InsertRemediationTaskAsync(new RemediationTaskRow(
            TaskId: taskId,
            RunId: "2026-08-01-lint-prior",
            Title: $"Proposal {taskId}",
            Description: "Agent-authored proposal (verbatim).",
            TargetPath: null,
            State: state,
            ProposedAt: now,
            AuthorizedAt: state is "authorized" or "executing" ? now : null,
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
