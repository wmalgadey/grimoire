using System.Net;
using System.Net.Http.Json;
using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T041 (013-lint-agent, US3, SC-003) — a trigger while a Lint Run is active is rejected
/// immediately over HTTP (409, never queued); a scripted silent/hung agent process is
/// marked failed once the liveness window elapses, the leftover process is terminated,
/// and the Findings Report is persisted with <c>partial: true</c>. The HTTP-level
/// concurrency assertion is a genuine gap this task closes: <c>LintRunLifecycleTests</c>
/// (T015) and <c>LintTraceTests</c> (T029/T030) both verify the coordinator-level
/// <see cref="LintSubmissionResult"/>/span/report outcomes hermetically, but neither
/// exercises <c>LintSubmissionEndpoints</c> through an actual HTTP round trip — this file
/// adds the minimal Lint-only test host to do so (mirrors
/// <c>QueryTurnSubmissionApiTests.BuildHostAsync</c>'s idiom, scoped to just
/// <c>/api/lint-runs</c>, since Lint has no SignalR channel to wire).
/// </summary>
public class LintConcurrencyAndLivenessTests
{
    // ── SC-003: concurrent trigger rejected immediately, over HTTP, no queue ───────────

    [Fact]
    public async Task PostLintRun_WhileARunIsActive_Returns409_SecondRequestNeverDispatched()
    {
        var launcher = new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromSeconds(5));
        using var host = await BuildLintHostAsync(launcher);
        var client = host.GetTestClient();

        var first = await client.PostAsync("/api/lint-runs", content: null);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var second = await client.PostAsync("/api/lint-runs", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var secondJson = await second.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("lint_run_active", secondJson.GetProperty("code").GetString());

        // No queue: exactly one process was ever dispatched, never two.
        Assert.Single(launcher.LintRequests);
    }

    [Fact]
    public async Task PostLintRun_AfterThePriorRunCompletes_Returns202_ANewRunIsDispatched()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true);
        using var host = await BuildLintHostAsync(launcher);
        var client = host.GetTestClient();

        var first = await client.PostAsync("/api/lint-runs", content: null);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var firstJson = await first.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var firstRunId = firstJson.GetProperty("runId").GetString()!;

        await WaitUntilAsync(async () =>
        {
            var response = await client.GetAsync($"/api/lint-runs/{firstRunId}");
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return json.GetProperty("status").GetString() == "completed";
        });

        var second = await client.PostAsync("/api/lint-runs", content: null);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        Assert.Equal(2, launcher.LintRequests.Count);
    }

    // ── SC-003: silent/hung agent — liveness failure, process termination, partial report ─

    [Fact]
    public async Task SilentAgentProcess_BeyondLivenessWindow_MarkedFailed_LeftoverProcessTerminated_PartialReportPersisted()
    {
        // Mirrors QueryLivenessSupervisionTests'/LintTraceTests' established idiom: a
        // short *real* liveness window with the default TimeProvider, not a hand-rolled
        // fake TimeProvider — LintRunCoordinator's watchdog is a periodic
        // TimeProvider.CreateTimer callback, and every existing liveness test in this
        // codebase (Ingest/Query/Lint alike) already drives that path deterministically
        // this way; introducing fake-timer machinery compatible with a periodic timer
        // would be net-new untested complexity for no additional coverage.
        var shortWindow = TimeSpan.FromMilliseconds(100);
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var harness = LintCoordinatorHarness.Create(launcher, livenessWindow: shortWindow);

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;

        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", runId);
        // ... then silence: no heartbeat, no activity, no terminal event at all.

        var run = await harness.WaitForTerminalAsync(runId);

        Assert.Equal(LintRunStatus.Failed, run.Status);
        Assert.Contains("liveness", run.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(handle.Terminated, "The leftover agent process must be terminated on liveness failure.");

        var content = await File.ReadAllTextAsync(harness.Paths.FindingsReportPathFor(runId));
        Assert.Contains("outcome_state: failed", content, StringComparison.Ordinal);
        Assert.Contains("partial: true", content, StringComparison.Ordinal);
        // No findings were ever produced before the hang (Lint has no partial-narrative
        // stream, unlike Query's answer_chunk — its only output is one final narrative) —
        // the report says so honestly rather than fabricating any (Constitution Principle V).
        Assert.Contains("Run failed before completion.", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PipeCloseWithoutTerminalEvent_DoesNotTransition_UntilLivenessWindowFires()
    {
        var shortWindow = TimeSpan.FromMilliseconds(100);
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var harness = LintCoordinatorHarness.Create(launcher, livenessWindow: shortWindow);

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;

        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", runId);
        // Hard crash: the stdout pipe closes without a terminal event.
        handle.ClosePipe();

        // Per ADR-008 the pipe close itself is not a transition — the liveness window is.
        var run = await harness.WaitForTerminalAsync(runId);
        Assert.Equal(LintRunStatus.Failed, run.Status);
        Assert.Contains("liveness", run.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    // ── shared setup: a minimal Lint-only HTTP host (no SignalR — Lint has no channel) ──

    internal static async Task<IHost> BuildLintHostAsync(FakeAgentProcessLauncher launcher, TimeSpan? livenessWindow = null)
    {
        var resolvedPaths = BuildResolvedPaths(CreateTempRoot());

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddSingleton<IAgentProcessLauncher>(launcher);
                    services.AddSingleton(resolvedPaths);
                    services.AddSingleton<FindingsReportStore>(sp => new FindingsReportStore(
                        resolvedPaths, NullLogger<FindingsReportStore>.Instance));
                    services.AddSingleton<LintRunCoordinator>(sp => new LintRunCoordinator(
                        sp.GetRequiredService<IAgentProcessLauncher>(),
                        sp.GetRequiredService<FindingsReportStore>(),
                        resolvedPaths,
                        livenessWindow: livenessWindow,
                        logger: NullLogger<LintRunCoordinator>.Instance));
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

        return await hostBuilder.StartAsync();
    }

    private static string CreateTempRoot()
        => Path.Combine(Path.GetTempPath(), $"lint-concurrency-liveness-{Guid.NewGuid():N}");

    private static ResolvedGrimoirePaths BuildResolvedPaths(string root)
    {
        var resolved = TestResolvedGrimoirePathsFactory.Create(root);
        Directory.CreateDirectory(resolved.FindingsDir);
        return resolved;
    }

    // 019-fast-test-tier (ADR-021 R4): thin wrapper over the shared PollAsync helper —
    // kept as a same-signature local method so every call site above is unchanged.
    private static Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 5000) =>
        PollAsync.WaitAsync(condition, TimeSpan.FromMilliseconds(timeoutMs), "Condition was not met within the timeout.", TimeSpan.FromMilliseconds(20));
}
