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

    // ── T021 (015-lint-board-parity, US3) — remediation_task entries on the board and
    // the independent list/detail endpoints (contracts/remediation-task-api.md) ─────────

    [Fact]
    public async Task RemediationRows_AppearOnTheBoard_AsTypedEntries_WithContractFieldSet()
    {
        using var harness = await BoardHostHarness.CreateAsync();
        var proposedAt = DateTimeOffset.UtcNow;
        await harness.Repository.InsertRemediationTaskAsync(MakeRemediationRow(
            "2026-08-01-remediation-boardentry000000000a", proposedAt: proposedAt));
        await harness.Repository.InsertRemediationTaskAsync(MakeRemediationRow(
            "2026-08-01-remediation-boardentry000000000b", state: "failed",
            outcomeReason: "The guarded write was denied.", proposedAt: proposedAt.AddSeconds(1)));

        var client = harness.Host.GetTestClient();
        using var board = JsonDocument.Parse(await client.GetStringAsync("/api/board"));
        var entries = board.RootElement.GetProperty("entries").EnumerateArray()
            .Where(e => e.GetProperty("kind").GetString() == "remediation_task")
            .ToList();

        // Non-terminal AND terminal tasks stay visible on the board
        // (contracts/lint-board-api.md `remediation_task` bullet).
        Assert.Equal(2, entries.Count);

        var proposed = Assert.Single(entries,
            e => e.GetProperty("taskId").GetString() == "2026-08-01-remediation-boardentry000000000a");
        Assert.Equal("2026-08-01-lint-boardparity", proposed.GetProperty("runId").GetString());
        Assert.Equal("Add missing tags to runtime-paths page", proposed.GetProperty("title").GetString());
        Assert.Equal("proposed", proposed.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, proposed.GetProperty("queuePosition").ValueKind);
        Assert.Equal(JsonValueKind.Null, proposed.GetProperty("outcomeReason").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, proposed.GetProperty("proposedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, proposed.GetProperty("updatedAt").ValueKind);

        // Board entries carry the list-entry field set minus description/targetPath bulk
        // detail (the card links to the detail endpoint) — contracts/lint-board-api.md.
        var fields = proposed.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("description", fields);
        Assert.DoesNotContain("targetPath", fields);

        // FR-005: the failure reason is surfaced on the board entry itself.
        var failed = Assert.Single(entries,
            e => e.GetProperty("taskId").GetString() == "2026-08-01-remediation-boardentry000000000b");
        Assert.Equal("failed", failed.GetProperty("state").GetString());
        Assert.Equal("The guarded write was denied.", failed.GetProperty("outcomeReason").GetString());
    }

    [Fact]
    public async Task RemediationTasks_AreIndependentlyListable_PerContract()
    {
        using var harness = await BoardHostHarness.CreateAsync();
        var proposedAt = DateTimeOffset.UtcNow;
        await harness.Repository.InsertRemediationTaskAsync(MakeRemediationRow(
            "2026-08-01-remediation-list0000000000000000a", proposedAt: proposedAt));
        // Two authorized rows: FIFO queue positions are 1-based, ordered by authorized_at
        // (FR-017, contracts/remediation-task-api.md `queuePosition`).
        await harness.Repository.InsertRemediationTaskAsync(MakeRemediationRow(
            "2026-08-01-remediation-list0000000000000000b", state: "authorized",
            authorizedAt: proposedAt.AddMinutes(2), proposedAt: proposedAt.AddSeconds(1)));
        await harness.Repository.InsertRemediationTaskAsync(MakeRemediationRow(
            "2026-08-01-remediation-list0000000000000000c", state: "authorized",
            authorizedAt: proposedAt.AddMinutes(1), proposedAt: proposedAt.AddSeconds(2),
            runId: "2026-08-01-lint-otherrun"));

        var client = harness.Host.GetTestClient();
        using var list = JsonDocument.Parse(await client.GetStringAsync("/api/remediation-tasks"));
        var tasks = list.RootElement.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(3, tasks.Count);

        var proposed = Assert.Single(tasks,
            t => t.GetProperty("taskId").GetString() == "2026-08-01-remediation-list0000000000000000a");
        Assert.Equal("2026-08-01-lint-boardparity", proposed.GetProperty("runId").GetString());
        Assert.Equal("Add missing tags to runtime-paths page", proposed.GetProperty("title").GetString());
        Assert.Equal("The page concepts/runtime-paths.md has no tags frontmatter.",
            proposed.GetProperty("description").GetString());
        Assert.Equal("concepts/runtime-paths.md", proposed.GetProperty("targetPath").GetString());
        Assert.Equal("proposed", proposed.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, proposed.GetProperty("authorizedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, proposed.GetProperty("queuePosition").ValueKind);
        Assert.Equal(JsonValueKind.Null, proposed.GetProperty("outcomeReason").ValueKind);

        // authorized_at defines FIFO order: the earlier authorization is position 1.
        var laterAuthorized = Assert.Single(tasks,
            t => t.GetProperty("taskId").GetString() == "2026-08-01-remediation-list0000000000000000b");
        Assert.Equal(2, laterAuthorized.GetProperty("queuePosition").GetInt32());
        var earlierAuthorized = Assert.Single(tasks,
            t => t.GetProperty("taskId").GetString() == "2026-08-01-remediation-list0000000000000000c");
        Assert.Equal(1, earlierAuthorized.GetProperty("queuePosition").GetInt32());

        // runId query parameter restricts to one originating run.
        using var filtered = JsonDocument.Parse(
            await client.GetStringAsync("/api/remediation-tasks?runId=2026-08-01-lint-otherrun"));
        var filteredTask = Assert.Single(filtered.RootElement.GetProperty("tasks").EnumerateArray().ToList());
        Assert.Equal("2026-08-01-remediation-list0000000000000000c", filteredTask.GetProperty("taskId").GetString());
    }

    [Fact]
    public async Task RemediationTaskDetail_IncludesRecordDerivedHistory_AndAllListFields()
    {
        using var harness = await BoardHostHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-detail000000000000000";
        var proposedAt = DateTimeOffset.UtcNow;
        await harness.Repository.InsertRemediationTaskAsync(MakeRemediationRow(taskId, proposedAt: proposedAt));
        await harness.RecordStore.CreateAsync(
            taskId, "2026-08-01-lint-boardparity", proposedAt,
            "Add missing tags to runtime-paths page",
            "The page concepts/runtime-paths.md has no tags frontmatter.",
            "concepts/runtime-paths.md");
        var attachedAt = proposedAt.AddMinutes(1);
        await harness.RecordStore.AppendContextAsync(
            taskId, "Use the tag taxonomy from wiki/index.md, not free-form tags.", attachedAt);

        var client = harness.Host.GetTestClient();
        var response = await client.GetAsync($"/api/remediation-tasks/{taskId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = detail.RootElement;
        Assert.Equal(taskId, root.GetProperty("taskId").GetString());
        Assert.Equal("proposed", root.GetProperty("state").GetString());
        Assert.Equal("The page concepts/runtime-paths.md has no tags frontmatter.",
            root.GetProperty("description").GetString());

        // attachedContext is sourced from the task's record (FR-011/FR-014).
        var context = Assert.Single(root.GetProperty("attachedContext").EnumerateArray().ToList());
        Assert.Equal("Use the tag taxonomy from wiki/index.md, not free-form tags.",
            context.GetProperty("content").GetString());
        Assert.NotEqual(JsonValueKind.Null, context.GetProperty("attachedAt").ValueKind);

        Assert.False(root.GetProperty("messageTurnActive").GetBoolean());

        // Unknown task ⇒ 404 in the shared error envelope (ADR-026). The detail is authored
        // prose rather than the old interpolated "Remediation task 'no-such-task' was not
        // found." — echoing the requested id back told the user nothing they did not type.
        var notFound = await client.GetAsync("/api/remediation-tasks/no-such-task");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        using var notFoundBody = JsonDocument.Parse(await notFound.Content.ReadAsStringAsync());
        Assert.Equal("remediation_task_not_found", notFoundBody.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(notFoundBody.RootElement.GetProperty("detail").GetString()));
    }

    private static RemediationTaskRow MakeRemediationRow(
        string taskId,
        string state = "proposed",
        string runId = "2026-08-01-lint-boardparity",
        string? outcomeReason = null,
        DateTimeOffset? authorizedAt = null,
        DateTimeOffset? proposedAt = null)
    {
        var at = proposedAt ?? DateTimeOffset.UtcNow;
        return new RemediationTaskRow(
            TaskId: taskId,
            RunId: runId,
            Title: "Add missing tags to runtime-paths page",
            Description: "The page concepts/runtime-paths.md has no tags frontmatter.",
            TargetPath: "concepts/runtime-paths.md",
            State: state,
            ProposedAt: at,
            AuthorizedAt: authorizedAt,
            OutcomeReason: outcomeReason,
            UpdatedAt: authorizedAt ?? at);
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
        var parsed = IngestTaskArtifactFrontmatter.TryParse(content);
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

    /// <summary>T021: seeds `remediation_tasks` rows the board/list/detail endpoints fold in.</summary>
    public OperationalStateRepository Repository => Host.Services.GetRequiredService<OperationalStateRepository>();

    /// <summary>T021: seeds the record-derived detail history (attached context, FR-011/FR-014).</summary>
    public RemediationTaskRecordStore RecordStore => Host.Services.GetRequiredService<RemediationTaskRecordStore>();

    public static async Task<BoardHostHarness> CreateAsync(FakeAgentProcessLauncher? launcher = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-board-composite-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.TasksDir);
        Directory.CreateDirectory(paths.FindingsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.IndexPath)!);
        File.WriteAllText(paths.IndexPath, "# Index\n");
        File.WriteAllText(paths.LogPath, string.Empty);

        var contentPaths = IngestContentPaths.FromResolved(paths);
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
                    services.AddSingleton<IngestKanbanBoardProjectionStore>();
                    // Registered so minimal-API parameter inference binds the mapped (but
                    // uninvoked) ingest submission handlers' parameters as services — the
                    // ingest group is mapped solely for the FR-015 field-parity assertion.
                    services.AddSingleton<IngestSubmissionValidator>();
                    services.AddSingleton<IngestTaskRecordReadModel>();
                    services.AddSingleton<IngestSourceArtifactStore>(sp =>
                        new IngestSourceArtifactStore(IngestRawStoragePaths.FromResolved(paths)));
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
                        new HubIngestTaskArtifactWriter(),
                        contentPaths,
                        paths,
                        logger: NullLogger<IngestRunCoordinator>.Instance));
                    services.AddSingleton<FindingsReportStore>(sp =>
                        new FindingsReportStore(paths, NullLogger<FindingsReportStore>.Instance));
                    services.AddSingleton<LintRunCoordinator>(sp => new LintRunCoordinator(
                        sp.GetRequiredService<Grimoire.Hub.AgentDispatch.IAgentProcessLauncher>(),
                        sp.GetRequiredService<FindingsReportStore>(),
                        paths,
                        logger: NullLogger<LintRunCoordinator>.Instance));
                    // T021/T024 (US3): the remediation task record store backing the
                    // detail endpoint's record-derived history.
                    services.AddSingleton<RemediationTaskRecordStore>(_ => new RemediationTaskRecordStore(paths));
                    // T033 (US4): the mapped-but-unexercised-in-this-file authorize/
                    // dismiss/withdraw handlers still need their DI parameters bindable
                    // for Minimal API's group-wide endpoint-metadata inference to succeed
                    // at startup, same reasoning as the ingest group's services above.
                    services.AddSingleton<RemediationLifecyclePublisher>(sp => new RemediationLifecyclePublisher(
                        sp.GetRequiredService<IHubContext<RemediationLifecycleHub>>()));
                    services.AddSingleton<RemediationRunCoordinator>(sp => new RemediationRunCoordinator(
                        repository,
                        sp.GetRequiredService<Grimoire.Hub.AgentDispatch.IAgentProcessLauncher>(),
                        sp.GetRequiredService<RemediationLifecyclePublisher>(),
                        sp.GetRequiredService<RemediationTaskRecordStore>(),
                        paths,
                        logger: NullLogger<RemediationRunCoordinator>.Instance));
                    // T041 (US5): same reasoning as RemediationRunCoordinator above — the
                    // mapped-but-unexercised-in-this-file context/messages handlers still
                    // need this bindable for Minimal API's group-wide endpoint-metadata
                    // inference to succeed at startup (unregistered ⇒ inferred as [FromBody]).
                    services.AddSingleton<RemediationMessageTurnCoordinator>(sp => new RemediationMessageTurnCoordinator(
                        sp.GetRequiredService<Grimoire.Hub.AgentDispatch.IAgentProcessLauncher>(),
                        sp.GetRequiredService<RemediationLifecyclePublisher>(),
                        sp.GetRequiredService<RemediationTaskRecordStore>(),
                        paths,
                        logger: NullLogger<RemediationMessageTurnCoordinator>.Instance));
                    // 018-hub-cli-commands T022: same reasoning as RemediationRunCoordinator
                    // above — the mapped-but-unexercised-in-this-file authorize/dismiss/
                    // withdraw handlers now delegate to this service and need it bindable
                    // for Minimal API's group-wide endpoint-metadata inference to succeed
                    // at startup.
                    services.AddSingleton<RemediationTaskTransitionService>(sp => new RemediationTaskTransitionService(
                        repository,
                        sp.GetRequiredService<RemediationLifecyclePublisher>(),
                        sp.GetRequiredService<RemediationRunCoordinator>(),
                        sp.GetRequiredService<RemediationTaskRecordStore>(),
                        NullLogger<RemediationLifecyclePublisher>.Instance));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGroup("/api/board").MapBoardEndpoints();
                        endpoints.MapGroup("/api/ingest-submissions").MapIngestSubmissionEndpoints();
                        endpoints.MapGroup("/api/remediation-tasks").MapRemediationTaskEndpoints();
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
        LintRunState? run = null;
        await PollAsync.WaitAsync(
            () =>
            {
                run = LintCoordinator.GetRun(runId);
                return run is { IsTerminal: true } && run.FindingsReportPath is not null;
            },
            TimeSpan.FromSeconds(10),
            () => $"Lint run '{runId}' did not reach a terminal state with a Findings Report in time (last seen: {run?.IsTerminal.ToString() ?? "not found"}).");

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
