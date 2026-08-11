using System.Text.RegularExpressions;
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
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T020 (015-lint-board-parity, US3, FR-007) — a fake lint agent completing with N
/// <c>proposedActions</c> materializes N <c>remediation_tasks</c> rows in
/// <c>proposed</c> state plus N task records, all of which exist <b>before</b> the lint
/// run's terminal lifecycle broadcast is published (FR-007 ordering guarantee,
/// data-model.md "Proposal materialization gates completion"); title/description/
/// targetPath are stored verbatim — never filtered, merged, or rewritten by the harness
/// (Principle V); an empty or absent list creates zero rows and the run still completes
/// (spec US3 scenario 2); and every materialized card produces its own
/// <c>remediationTaskLifecycleChanged</c> (<c>fromState: null → "proposed"</c>) broadcast
/// on <c>/hubs/remediation-lifecycle</c> (contracts/remediation-lifecycle-events.md).
/// Hermetic — fake <c>IAgentProcessLauncher</c>, real SQLite + real SignalR, no live LLM.
/// </summary>
public class LintRemediationProposalMaterializationTests
{
    // Deliberately markdown-/injection-flavored verbatim content (Principle V: the
    // harness must store it byte-for-byte, and the record format must survive it).
    private static readonly object[] TwoProposals =
    [
        new
        {
            title = "Add missing tags to [[runtime-paths]]",
            description = "The page concepts/runtime-paths.md has no `tags` frontmatter.\n\nPropose adding tags: [tech/dotnet, concept/paths].",
            targetPath = "concepts/runtime-paths.md",
        },
        new
        {
            title = "Cross-reference [[credential-scoping]] and [[agent-dispatch]] <!-- grimoire:proposal -->",
            description = "Both pages describe the same launch flow but do not link each other.",
        },
    ];

    [Fact]
    public async Task CompletedRunWithProposals_MaterializesRowsAndRecords_BeforeTerminalBroadcast()
    {
        await using var app = await StartHubHostAsync();
        var baseUrl = app.Urls.First();

        await using var harness = await LintRemediationMaterializationHarness.CreateAsync(app);
        harness.Launcher.ScriptedLintTerminalMetadata = new Dictionary<string, object?>
        {
            ["proposedActions"] = TwoProposals,
        };

        // FR-007 ordering pin: snapshot the persisted rows and record files at the moment
        // the terminal lint broadcast arrives — materialization must already be complete.
        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hubs/lint-lifecycle")
            .Build();

        var terminalObserved = new TaskCompletionSource<(IReadOnlyList<RemediationTaskRow> Rows, string[] RecordFiles)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<LintRunLifecycleEvent>("lintRunLifecycleChanged", e =>
        {
            if (e.ToStatus is "completed" or "failed")
            {
                var rows = harness.Repository.GetRemediationTasksAsync().GetAwaiter().GetResult();
                var recordFiles = Directory.Exists(harness.Paths.RemediationTasksDir)
                    ? Directory.GetFiles(harness.Paths.RemediationTasksDir, "*.md")
                    : [];
                terminalObserved.TrySetResult((rows, recordFiles));
            }
        });
        await connection.StartAsync();

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;

        var (rowsAtBroadcast, recordFilesAtBroadcast) = await terminalObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // N proposals ⇒ N proposed rows, present at broadcast time (FR-007).
        Assert.Equal(2, rowsAtBroadcast.Count);
        Assert.All(rowsAtBroadcast, row => Assert.Equal("proposed", row.State));
        Assert.All(rowsAtBroadcast, row => Assert.Equal(runId, row.RunId));

        // Task-id shape per data-model.md: {yyyy-MM-dd}-remediation-{guid:N} truncated to 44.
        Assert.All(rowsAtBroadcast, row =>
        {
            Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}-remediation-[0-9a-f]+$"), row.TaskId);
            Assert.Equal(44, row.TaskId.Length);
        });
        Assert.Equal(2, rowsAtBroadcast.Select(r => r.TaskId).Distinct().Count());

        // Principle V: agent-authored proposal text is stored verbatim.
        var first = Assert.Single(rowsAtBroadcast, r => r.Title == "Add missing tags to [[runtime-paths]]");
        Assert.Equal(
            "The page concepts/runtime-paths.md has no `tags` frontmatter.\n\nPropose adding tags: [tech/dotnet, concept/paths].",
            first.Description);
        Assert.Equal("concepts/runtime-paths.md", first.TargetPath);

        var second = Assert.Single(rowsAtBroadcast,
            r => r.Title == "Cross-reference [[credential-scoping]] and [[agent-dispatch]] <!-- grimoire:proposal -->");
        Assert.Null(second.TargetPath);

        // N task records existed at broadcast time, and each parses back to the verbatim proposal.
        Assert.Equal(2, recordFilesAtBroadcast.Length);
        foreach (var row in rowsAtBroadcast)
        {
            Assert.Contains(harness.Paths.RemediationTaskRecordPathFor(row.TaskId), recordFilesAtBroadcast);

            var parsed = Assert.IsType<RemediationTaskRecordParseResult.Parsed>(
                await harness.RecordStore.ReadAsync(row.TaskId));
            var proposal = Assert.IsType<RemediationTaskRecordEntry.Proposal>(Assert.Single(parsed.Entries));
            Assert.Equal(row.Title, proposal.Title);
            Assert.Equal(row.Description, proposal.Description);
            Assert.Equal(row.TargetPath, proposal.TargetPath);
        }

        var run = await harness.WaitForTerminalAsync(runId);
        Assert.Equal(LintRunStatus.Completed, run.Status);

        await connection.StopAsync();
        await app.StopAsync();
    }

    [Fact]
    public async Task EmptyProposedActions_CreatesNoRows_AndTheRunStillCompletes()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await LintRemediationMaterializationHarness.CreateAsync(app);
        harness.Launcher.ScriptedLintTerminalMetadata = new Dictionary<string, object?>
        {
            ["proposedActions"] = Array.Empty<object>(),
        };

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);

        var run = await harness.WaitForTerminalAsync(accepted.Run.RunId);
        Assert.Equal(LintRunStatus.Completed, run.Status);
        Assert.Empty(await harness.Repository.GetRemediationTasksAsync());

        await app.StopAsync();
    }

    [Fact]
    public async Task AbsentProposedActions_CreatesNoRows_AndTheRunStillCompletes()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await LintRemediationMaterializationHarness.CreateAsync(app);
        // No scripted metadata at all: the fake emits a plain pre-015 completed event.

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);

        var run = await harness.WaitForTerminalAsync(accepted.Run.RunId);
        Assert.Equal(LintRunStatus.Completed, run.Status);
        Assert.Empty(await harness.Repository.GetRemediationTasksAsync());

        await app.StopAsync();
    }

    [Fact]
    public async Task EachMaterializedTask_BroadcastsProposedLifecycleEvent_OnRemediationHub()
    {
        await using var app = await StartHubHostAsync();
        var baseUrl = app.Urls.First();

        await using var harness = await LintRemediationMaterializationHarness.CreateAsync(app);
        harness.Launcher.ScriptedLintTerminalMetadata = new Dictionary<string, object?>
        {
            ["proposedActions"] = TwoProposals,
        };

        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hubs/remediation-lifecycle")
            .Build();

        var received = new List<RemediationTaskLifecycleEvent>();
        var lockObj = new object();
        connection.On<RemediationTaskLifecycleEvent>("remediationTaskLifecycleChanged", e =>
        {
            lock (lockObj) { received.Add(e); }
        });
        await connection.StartAsync();

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;
        await harness.WaitForTerminalAsync(runId);

        await PollAsync.WaitAsync(
            () =>
            {
                lock (lockObj)
                {
                    return received.Count >= 2;
                }
            },
            TimeSpan.FromSeconds(10),
            "Expected at least 2 remediationTaskLifecycleChanged broadcasts within 10s.");

        List<RemediationTaskLifecycleEvent> snapshot;
        lock (lockObj) { snapshot = [.. received]; }

        // One materialization broadcast per card: fromState null → "proposed"
        // (contracts/remediation-lifecycle-events.md `remediationTaskLifecycleChanged`).
        Assert.Equal(2, snapshot.Count);
        Assert.All(snapshot, e => Assert.False(string.IsNullOrEmpty(e.EventId)));
        Assert.All(snapshot, e => Assert.Equal(runId, e.RunId));
        Assert.All(snapshot, e => Assert.Null(e.FromState));
        Assert.All(snapshot, e => Assert.Equal("proposed", e.ToState));
        Assert.All(snapshot, e => Assert.Null(e.QueuePosition));
        Assert.All(snapshot, e => Assert.Null(e.OutcomeReason));

        var rows = await harness.Repository.GetRemediationTasksAsync();
        Assert.Equal(
            rows.Select(r => r.TaskId).Order(),
            snapshot.Select(e => e.TaskId).Order());

        await connection.StopAsync();
        await app.StopAsync();
    }

    private static async Task<WebApplication> StartHubHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        var app = builder.Build();
        app.MapHub<LintLifecycleHub>("/hubs/lint-lifecycle");
        app.MapHub<RemediationLifecycleHub>("/hubs/remediation-lifecycle");
        await app.StartAsync();
        return app;
    }
}

/// <summary>
/// Coordinator harness with the full US3 materialization wiring — real SQLite
/// operational state, real record store, and both lifecycle publishers bound to the
/// caller's SignalR host (mirrors <see cref="LintCoordinatorHarness"/>, which stays
/// materialization-free for the pre-015 lint tests).
/// </summary>
internal sealed class LintRemediationMaterializationHarness : IAsyncDisposable
{
    private readonly string _root;

    private LintRemediationMaterializationHarness(
        string root,
        ResolvedGrimoirePaths paths,
        FakeAgentProcessLauncher launcher,
        OperationalStateRepository repository,
        RemediationTaskRecordStore recordStore,
        LintRunCoordinator coordinator)
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
    public LintRunCoordinator Coordinator { get; }

    public static async Task<LintRemediationMaterializationHarness> CreateAsync(WebApplication hubHost)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-remediation-materialization-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.FindingsDir);

        var launcher = new FakeAgentProcessLauncher(autoPlay: true);
        var repository = new OperationalStateRepository(paths.StateDbPath);
        await repository.InitializeAsync();
        var recordStore = new RemediationTaskRecordStore(paths);

        var lintPublisher = new LintLifecyclePublisher(
            hubHost.Services.GetRequiredService<IHubContext<LintLifecycleHub>>());
        var remediationPublisher = new RemediationLifecyclePublisher(
            hubHost.Services.GetRequiredService<IHubContext<RemediationLifecycleHub>>());

        var coordinator = new LintRunCoordinator(
            launcher,
            new FindingsReportStore(paths, NullLogger<FindingsReportStore>.Instance),
            paths,
            logger: NullLogger<LintRunCoordinator>.Instance,
            lifecyclePublisher: lintPublisher,
            stateRepository: repository,
            remediationRecordStore: recordStore,
            remediationLifecyclePublisher: remediationPublisher);

        return new LintRemediationMaterializationHarness(root, paths, launcher, repository, recordStore, coordinator);
    }

    public async Task<LintRunState> WaitForTerminalAsync(string runId)
    {
        LintRunState? run = null;
        await PollAsync.WaitAsync(
            () =>
            {
                run = Coordinator.GetRun(runId);
                return run is { IsTerminal: true } && run.FindingsReportPath is not null;
            },
            TimeSpan.FromSeconds(10),
            () => $"Lint run '{runId}' did not reach a terminal state with a Findings Report in time (last seen: {run?.IsTerminal.ToString() ?? "not found"}).");

        Assert.NotNull(run);
        Assert.True(run!.IsTerminal, $"Lint run '{runId}' did not reach a terminal state in time.");
        return run;
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
