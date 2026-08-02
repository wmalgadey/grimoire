using Grimoire.Hub.OperationalState;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T004 (015-lint-board-parity, ADR-018/ADR-003) — the <c>remediation_tasks</c> table in
/// <see cref="OperationalStateRepository"/> against a real SQLite file: insert/read
/// round-trips, state filtering, and the compare-and-swap transition semantics that
/// arbitrate the withdrawal-vs-execution race (first commit wins, affected-rows-based
/// result), plus the remediation queue-paused flag's independence from ingest's.
/// </summary>
public class RemediationTaskRepositoryTests
{
    private static async Task<OperationalStateRepository> CreateRepositoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-remtask-repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var repository = new OperationalStateRepository(Path.Combine(root, "operational-state.db"));
        await repository.InitializeAsync();
        return repository;
    }

    private static RemediationTaskRow MakeRow(
        string taskId = "2026-08-01-remediation-a1b2c3",
        string state = "proposed",
        string? targetPath = "wiki/runtime-paths.md",
        DateTimeOffset? proposedAt = null) => new(
        TaskId: taskId,
        RunId: "2026-08-01-lint-9f8e7d",
        Title: "Add missing tags to runtime-paths page",
        Description: "The page wiki/runtime-paths.md has no tags frontmatter.",
        TargetPath: targetPath,
        State: state,
        ProposedAt: proposedAt ?? DateTimeOffset.UtcNow,
        AuthorizedAt: null,
        OutcomeReason: null,
        UpdatedAt: proposedAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public async Task Insert_ThenGetAll_RoundTripsEveryField()
    {
        var repository = await CreateRepositoryAsync();
        var proposedAt = DateTimeOffset.UtcNow;
        var row = MakeRow(proposedAt: proposedAt);

        await repository.InsertRemediationTaskAsync(row);

        var all = await repository.GetRemediationTasksAsync();
        var stored = Assert.Single(all);
        Assert.Equal(row, stored);
    }

    [Fact]
    public async Task Insert_WithNullTargetPath_RoundTripsNull()
    {
        var repository = await CreateRepositoryAsync();

        await repository.InsertRemediationTaskAsync(MakeRow(targetPath: null));

        var stored = Assert.Single(await repository.GetRemediationTasksAsync());
        Assert.Null(stored.TargetPath);
    }

    [Fact]
    public async Task Insert_IsIdempotentPerTaskId()
    {
        var repository = await CreateRepositoryAsync();
        var row = MakeRow();

        await repository.InsertRemediationTaskAsync(row);
        await repository.InsertRemediationTaskAsync(row with { Title = "changed" });

        var stored = Assert.Single(await repository.GetRemediationTasksAsync());
        Assert.Equal(row.Title, stored.Title);
    }

    [Fact]
    public async Task GetByState_FiltersAndOrdersByProposalTime()
    {
        var repository = await CreateRepositoryAsync();
        var t0 = DateTimeOffset.UtcNow;
        await repository.InsertRemediationTaskAsync(MakeRow("2026-08-01-remediation-b", proposedAt: t0.AddSeconds(1)));
        await repository.InsertRemediationTaskAsync(MakeRow("2026-08-01-remediation-a", proposedAt: t0));
        await repository.InsertRemediationTaskAsync(MakeRow("2026-08-01-remediation-c", state: "dismissed", proposedAt: t0));

        var proposed = await repository.GetRemediationTasksAsync("proposed");
        var dismissed = await repository.GetRemediationTasksAsync("dismissed");

        Assert.Equal(["2026-08-01-remediation-a", "2026-08-01-remediation-b"], proposed.Select(r => r.TaskId));
        Assert.Equal(["2026-08-01-remediation-c"], dismissed.Select(r => r.TaskId));
    }

    [Fact]
    public async Task Transition_ToAuthorized_StampsAuthorizedAt()
    {
        var repository = await CreateRepositoryAsync();
        await repository.InsertRemediationTaskAsync(MakeRow());
        var authorizedAt = DateTimeOffset.UtcNow;

        var committed = await repository.TryTransitionRemediationTaskAsync(
            "2026-08-01-remediation-a1b2c3", "proposed", "authorized",
            outcomeReason: null, authorizedAt: authorizedAt, updatedAt: authorizedAt);

        Assert.True(committed);
        var stored = Assert.Single(await repository.GetRemediationTasksAsync());
        Assert.Equal("authorized", stored.State);
        Assert.Equal(authorizedAt, stored.AuthorizedAt);
        Assert.Equal(authorizedAt, stored.UpdatedAt);
    }

    [Fact]
    public async Task Transition_WithStaleFromState_IsRejected_AndLeavesTheRowUntouched()
    {
        var repository = await CreateRepositoryAsync();
        var row = MakeRow();
        await repository.InsertRemediationTaskAsync(row);

        var committed = await repository.TryTransitionRemediationTaskAsync(
            row.TaskId, "authorized", "executing",
            outcomeReason: null, authorizedAt: null, updatedAt: DateTimeOffset.UtcNow);

        Assert.False(committed);
        var stored = Assert.Single(await repository.GetRemediationTasksAsync());
        Assert.Equal(row, stored);
    }

    [Fact]
    public async Task WithdrawalRace_FirstCommitWins_LoserIsRejected()
    {
        // ADR-018 / spec Edge Cases: Authorized → Proposed (withdrawal) and
        // Authorized → Executing (dispatch) race on the same row; the persisted CAS is
        // the single arbiter — exactly one side commits.
        var repository = await CreateRepositoryAsync();
        await repository.InsertRemediationTaskAsync(MakeRow());
        var now = DateTimeOffset.UtcNow;
        await repository.TryTransitionRemediationTaskAsync(
            "2026-08-01-remediation-a1b2c3", "proposed", "authorized",
            outcomeReason: null, authorizedAt: now, updatedAt: now);

        var withdrawal = repository.TryTransitionRemediationTaskAsync(
            "2026-08-01-remediation-a1b2c3", "authorized", "proposed",
            outcomeReason: null, authorizedAt: null, updatedAt: DateTimeOffset.UtcNow);
        var dispatch = repository.TryTransitionRemediationTaskAsync(
            "2026-08-01-remediation-a1b2c3", "authorized", "executing",
            outcomeReason: null, authorizedAt: null, updatedAt: DateTimeOffset.UtcNow);
        var results = await Task.WhenAll(withdrawal, dispatch);

        Assert.Single(results, r => r);
        var stored = Assert.Single(await repository.GetRemediationTasksAsync());
        Assert.True(stored.State is "proposed" or "executing");
    }

    [Fact]
    public async Task Withdrawal_ClearsAuthorizedAt_ForAFreshQueuePositionLater()
    {
        var repository = await CreateRepositoryAsync();
        await repository.InsertRemediationTaskAsync(MakeRow());
        var now = DateTimeOffset.UtcNow;
        await repository.TryTransitionRemediationTaskAsync(
            "2026-08-01-remediation-a1b2c3", "proposed", "authorized",
            outcomeReason: null, authorizedAt: now, updatedAt: now);

        var committed = await repository.TryTransitionRemediationTaskAsync(
            "2026-08-01-remediation-a1b2c3", "authorized", "proposed",
            outcomeReason: null, authorizedAt: null, updatedAt: DateTimeOffset.UtcNow);

        Assert.True(committed);
        var stored = Assert.Single(await repository.GetRemediationTasksAsync());
        Assert.Equal("proposed", stored.State);
        Assert.Null(stored.AuthorizedAt);
    }

    [Fact]
    public async Task Transition_ToExecuting_LeavesAuthorizedAtUntouched()
    {
        var repository = await CreateRepositoryAsync();
        await repository.InsertRemediationTaskAsync(MakeRow());
        var authorizedAt = DateTimeOffset.UtcNow;
        await repository.TryTransitionRemediationTaskAsync(
            "2026-08-01-remediation-a1b2c3", "proposed", "authorized",
            outcomeReason: null, authorizedAt: authorizedAt, updatedAt: authorizedAt);

        await repository.TryTransitionRemediationTaskAsync(
            "2026-08-01-remediation-a1b2c3", "authorized", "executing",
            outcomeReason: null, authorizedAt: null, updatedAt: DateTimeOffset.UtcNow);

        var stored = Assert.Single(await repository.GetRemediationTasksAsync());
        Assert.Equal("executing", stored.State);
        Assert.Equal(authorizedAt, stored.AuthorizedAt);
    }

    [Fact]
    public async Task TerminalTransition_PersistsOutcomeReason_AndFirstTerminalWins()
    {
        var repository = await CreateRepositoryAsync();
        await repository.InsertRemediationTaskAsync(MakeRow(state: "executing"));

        var first = await repository.TryTransitionRemediationTaskAsync(
            "2026-08-01-remediation-a1b2c3", "executing", "not_applicable",
            outcomeReason: "Tags already present; proposal is moot.",
            authorizedAt: null, updatedAt: DateTimeOffset.UtcNow);
        var second = await repository.TryTransitionRemediationTaskAsync(
            "2026-08-01-remediation-a1b2c3", "executing", "failed",
            outcomeReason: "liveness window expired",
            authorizedAt: null, updatedAt: DateTimeOffset.UtcNow);

        Assert.True(first);
        Assert.False(second);
        var stored = Assert.Single(await repository.GetRemediationTasksAsync());
        Assert.Equal("not_applicable", stored.State);
        Assert.Equal("Tags already present; proposal is moot.", stored.OutcomeReason);
    }

    [Fact]
    public async Task RemediationQueuePausedFlag_IsIndependentOfIngestsQueuePausedFlag()
    {
        // Ingest's flag key by literal ("queue_paused", IngestRunCoordinator.QueuePausedFlag):
        // referencing the coordinator type here would make this cross-domain repository test
        // ingest-owned under the N1 naming rule (ADR-013) — the key value is the contract.
        const string ingestQueuePausedFlag = "queue_paused";
        var repository = await CreateRepositoryAsync();

        Assert.NotEqual(ingestQueuePausedFlag, OperationalStateRepository.RemediationQueuePausedFlag);

        await repository.SetFlagAsync(OperationalStateRepository.RemediationQueuePausedFlag, true);

        Assert.True(await repository.GetFlagAsync(OperationalStateRepository.RemediationQueuePausedFlag));
        Assert.False(await repository.GetFlagAsync(ingestQueuePausedFlag));

        await repository.SetFlagAsync(ingestQueuePausedFlag, true);
        await repository.SetFlagAsync(OperationalStateRepository.RemediationQueuePausedFlag, false);

        Assert.True(await repository.GetFlagAsync(ingestQueuePausedFlag));
        Assert.False(await repository.GetFlagAsync(OperationalStateRepository.RemediationQueuePausedFlag));
    }
}
