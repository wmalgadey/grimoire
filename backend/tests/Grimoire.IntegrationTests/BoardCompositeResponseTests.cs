using System.Net;
using System.Text.Json;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.Conversion;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.IngestTaskArtifact;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.Realtime;
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
/// T010 (015-lint-board-parity, US1) — the composite board initial-state response
/// (contracts/lint-board-api.md `GET /api/board`) carries the latest lint run as a
/// <c>kind: "lint_run"</c> typed entry (status/triggeredAt/completedAt/failureReason/
/// hasFindingsReport — the field set of <c>GET /api/lint-runs/{runId}</c>) alongside
/// ingest entries whose field set is exactly today's <c>GET /api/ingest-submissions</c>
/// rows plus the <c>kind</c> discriminator (FR-015). Covers the "no run ever" case (no
/// lint entry — the board offers the trigger control instead, US1 scenario 1) and the
/// pre-existing-active-run recovery edge case (a run triggered before the board loaded
/// appears as running).
/// </summary>
public class BoardCompositeResponseTests
{
    [Fact]
    public async Task NoLintRunEver_BoardHasNoLintEntry_AndIngestEntriesMatchTodaysRowsPlusKind()
    {
        using var harness = await BoardHostHarness.CreateAsync();
        WriteTaskArtifact(harness.TasksDir, "2026-08-01-ingest-boardtest1", "queued");

        var client = harness.Host.GetTestClient();

        var boardResponse = await client.GetAsync("/api/board");
        Assert.Equal(HttpStatusCode.OK, boardResponse.StatusCode);
        using var board = JsonDocument.Parse(await boardResponse.Content.ReadAsStringAsync());
        var entries = board.RootElement.GetProperty("entries").EnumerateArray().ToList();

        // No lint run has ever been triggered: no lint_run entry at all (US1 scenario 1 —
        // the board renders its "no lint activity yet" state with the trigger control).
        Assert.DoesNotContain(entries, e => e.GetProperty("kind").GetString() == "lint_run");

        var ingestEntry = Assert.Single(entries, e => e.GetProperty("kind").GetString() == "ingest_task");

        // FR-015: the ingest entry carries exactly the field set of today's
        // GET /api/ingest-submissions rows (verbatim values), plus only `kind`.
        var ingestRowsResponse = await client.GetAsync("/api/ingest-submissions/");
        Assert.Equal(HttpStatusCode.OK, ingestRowsResponse.StatusCode);
        using var ingestRows = JsonDocument.Parse(await ingestRowsResponse.Content.ReadAsStringAsync());
        var row = Assert.Single(ingestRows.RootElement.GetProperty("tasks").EnumerateArray().ToList());

        var rowFields = row.EnumerateObject().Select(p => p.Name).ToHashSet();
        var entryFields = ingestEntry.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(rowFields.Concat(["kind"]).Order(), entryFields.Order());

        foreach (var field in rowFields)
        {
            Assert.Equal(row.GetProperty(field).GetRawText(), ingestEntry.GetProperty(field).GetRawText());
        }
    }

    [Fact]
    public async Task PreExistingActiveRun_AppearsOnTheBoard_AsRunningLintEntry()
    {
        // Edge case (spec): a run active before the board was (re)opened must be
        // recovered from the initial-state response, not treated as never started.
        using var harness = await BoardHostHarness.CreateAsync(
            new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromSeconds(10)));

        var result = await harness.LintCoordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);

        var client = harness.Host.GetTestClient();
        using var board = JsonDocument.Parse(await client.GetStringAsync("/api/board"));
        var entries = board.RootElement.GetProperty("entries").EnumerateArray().ToList();

        var lintEntry = Assert.Single(entries, e => e.GetProperty("kind").GetString() == "lint_run");
        Assert.Equal(accepted.Run.RunId, lintEntry.GetProperty("runId").GetString());
        Assert.Equal("running", lintEntry.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, lintEntry.GetProperty("completedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, lintEntry.GetProperty("failureReason").ValueKind);
        Assert.False(lintEntry.GetProperty("hasFindingsReport").GetBoolean());
    }

    [Fact]
    public async Task CompletedRun_BoardEntryShowsCompleted_WithFindingsReport()
    {
        using var harness = await BoardHostHarness.CreateAsync();

        var result = await harness.LintCoordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        await harness.WaitForTerminalAsync(accepted.Run.RunId);

        var client = harness.Host.GetTestClient();
        using var board = JsonDocument.Parse(await client.GetStringAsync("/api/board"));
        var entries = board.RootElement.GetProperty("entries").EnumerateArray().ToList();

        var lintEntry = Assert.Single(entries, e => e.GetProperty("kind").GetString() == "lint_run");
        Assert.Equal(accepted.Run.RunId, lintEntry.GetProperty("runId").GetString());
        Assert.Equal("completed", lintEntry.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, lintEntry.GetProperty("completedAt").ValueKind);
        Assert.True(lintEntry.GetProperty("hasFindingsReport").GetBoolean());
    }

    [Fact]
    public async Task FailedRun_BoardEntryShowsFailed_WithFailureReason()
    {
        const string reason = "Lint agent run failed: policy could not be loaded.";
        using var harness = await BoardHostHarness.CreateAsync(
            new FakeAgentProcessLauncher(terminalStatus: "failed", failureReason: reason, autoPlay: true));

        var result = await harness.LintCoordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        await harness.WaitForTerminalAsync(accepted.Run.RunId);

        var client = harness.Host.GetTestClient();
        using var board = JsonDocument.Parse(await client.GetStringAsync("/api/board"));
        var entries = board.RootElement.GetProperty("entries").EnumerateArray().ToList();

        // FR-005: the failure reason is surfaced on the board entry itself.
        var lintEntry = Assert.Single(entries, e => e.GetProperty("kind").GetString() == "lint_run");
        Assert.Equal("failed", lintEntry.GetProperty("status").GetString());
        Assert.Equal(reason, lintEntry.GetProperty("failureReason").GetString());
    }

    private static void WriteTaskArtifact(string tasksDir, string taskId, string status)
    {
        Directory.CreateDirectory(tasksDir);
        var content =
            $"""
            ---
            task_id: {taskId}
            type: ingest
            status: {status}
            agent: ingest
            started_at: {DateTimeOffset.UtcNow:O}
            completed_at: null
            source_ref: "raw/sources/{taskId}.md"
            failure_reason: null
            ---

            Test task artifact ({status}).
            """;
        File.WriteAllText(Path.Combine(tasksDir, $"{taskId}.md"), content);

        // Guard the fixture against drifting from the real ingest artifact shape: the
        // board projection only lists artifacts ingest's own frontmatter reader accepts
        // (this test is genuinely cross-agent — ingest parity + lint entry composition).
        var parsed = TaskArtifactFrontmatter.TryParse(content);
        Assert.NotNull(parsed);
        Assert.Equal(taskId, parsed!.TaskId);
    }
}

/// <summary>
/// TestServer host wiring the composite board endpoint plus the unchanged ingest board
/// endpoint (for the FR-015 field-parity assertion) against a temp root — mirrors
/// <see cref="QueryTurnSubmissionApiTests"/>' BuildHostAsync idiom.
/// </summary>
internal sealed class BoardHostHarness : IDisposable
{
    private readonly string _root;

    private BoardHostHarness(string root, IHost host, LintRunCoordinator lintCoordinator, ResolvedGrimoirePaths paths)
    {
        _root = root;
        Host = host;
        LintCoordinator = lintCoordinator;
        Paths = paths;
    }

    public IHost Host { get; }
    public LintRunCoordinator LintCoordinator { get; }
    public ResolvedGrimoirePaths Paths { get; }
    public string TasksDir => Paths.TasksDir;

    public static async Task<BoardHostHarness> CreateAsync(FakeAgentProcessLauncher? launcher = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-board-composite-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.TasksDir);
        Directory.CreateDirectory(paths.FindingsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.IndexPath)!);
        File.WriteAllText(paths.IndexPath, "# Index\n");
        File.WriteAllText(paths.LogPath, string.Empty);

        var contentPaths = ContentRootPaths.FromResolved(paths);
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
                    services.AddSignalR();
                    services.AddSingleton(paths);
                    services.AddSingleton(contentPaths);
                    services.AddSingleton(repository);
                    services.AddSingleton<Grimoire.Hub.AgentDispatch.IAgentProcessLauncher>(effectiveLauncher);
                    services.AddSingleton<KanbanBoardProjectionStore>();
                    // Registered so minimal-API parameter inference binds the mapped (but
                    // uninvoked) ingest submission handlers' parameters as services — the
                    // ingest group is mapped solely for the FR-015 field-parity assertion.
                    services.AddSingleton<IngestSubmissionValidator>();
                    services.AddSingleton<TaskRecordReadModel>();
                    services.AddSingleton<SourceArtifactStore>(sp =>
                        new SourceArtifactStore(RawStoragePaths.FromResolved(paths)));
                    services.AddSingleton<IMarkdownConverter>(FakeMarkdownConverter.Succeeding("# converted\n"));
                    services.AddSingleton<IUrlContentFetcher>(
                        FakeUrlContentFetcher.Succeeding("<html></html>"u8.ToArray()));
                    services.AddSingleton<IngestSubmissionPipeline>();
                    services.AddSingleton<IngestLifecyclePublisher>(sp => new IngestLifecyclePublisher(
                        sp.GetRequiredService<IHubContext<IngestLifecycleHub>>()));
                    services.AddSingleton<IngestRunCoordinator>(sp => new IngestRunCoordinator(
                        repository,
                        sp.GetRequiredService<Grimoire.Hub.AgentDispatch.IAgentProcessLauncher>(),
                        sp.GetRequiredService<IngestLifecyclePublisher>(),
                        new HubTaskArtifactWriter(),
                        contentPaths,
                        logger: NullLogger<IngestRunCoordinator>.Instance));
                    services.AddSingleton<FindingsReportStore>(sp =>
                        new FindingsReportStore(paths, NullLogger<FindingsReportStore>.Instance));
                    services.AddSingleton<LintRunCoordinator>(sp => new LintRunCoordinator(
                        sp.GetRequiredService<Grimoire.Hub.AgentDispatch.IAgentProcessLauncher>(),
                        sp.GetRequiredService<FindingsReportStore>(),
                        paths,
                        logger: NullLogger<LintRunCoordinator>.Instance));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGroup("/api/board").MapBoardEndpoints();
                        endpoints.MapGroup("/api/ingest-submissions").MapIngestSubmissionEndpoints();
                    });
                });
            });

        var host = await hostBuilder.StartAsync();
        var lintCoordinator = host.Services.GetRequiredService<LintRunCoordinator>();
        await host.Services.GetRequiredService<IngestRunCoordinator>().InitializeAsync();

        return new BoardHostHarness(root, host, lintCoordinator, paths);
    }

    public async Task<LintRunState> WaitForTerminalAsync(string runId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        LintRunState? run = null;
        while (DateTime.UtcNow < deadline)
        {
            run = LintCoordinator.GetRun(runId);
            if (run is { IsTerminal: true } && run.FindingsReportPath is not null)
            {
                return run;
            }
            await Task.Delay(25);
        }

        Assert.NotNull(run);
        Assert.True(run!.IsTerminal, $"Lint run '{runId}' did not reach a terminal state in time.");
        return run;
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
