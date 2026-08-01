using Grimoire.Hub.OperationalState;
using Grimoire.Hub.RemediationTasks;

namespace Grimoire.Domain.UnitTests;

/// <summary>
/// T005 (015-lint-board-parity, ADR-018 normative) — <see cref="RemediationActionTask"/>
/// state-machine invariants: every valid edge of the data-model.md transition table,
/// every invalid edge rejected, first-terminal-transition-wins idempotence,
/// <c>authorized_at</c> stamp/clear semantics, and the mandatory-<c>outcome_reason</c>
/// rule for <c>Failed</c>/<c>NotApplicable</c>. Complex domain invariants — unit tests
/// justified per Constitution Principle II.
/// </summary>
public class RemediationActionTaskStateMachineTests
{
    private static readonly DateTimeOffset _t0 = DateTimeOffset.Parse("2026-08-01T09:00:00Z");

    private static RemediationActionTask MakeTask() => new(
        taskId: "2026-08-01-remediation-a1b2c3",
        runId: "2026-08-01-lint-9f8e7d",
        title: "Add missing tags to runtime-paths page",
        description: "The page wiki/runtime-paths.md has no tags frontmatter.",
        targetPath: "wiki/runtime-paths.md",
        proposedAt: _t0);

    /// <summary>Drives a fresh task along valid edges into the requested state.</summary>
    private static RemediationActionTask MakeTaskIn(RemediationTaskState state)
    {
        var task = MakeTask();
        switch (state)
        {
            case RemediationTaskState.Proposed:
                break;
            case RemediationTaskState.Authorized:
                Assert.True(task.TryTransitionTo(RemediationTaskState.Authorized, _t0.AddMinutes(1)));
                break;
            case RemediationTaskState.Executing:
                Assert.True(task.TryTransitionTo(RemediationTaskState.Authorized, _t0.AddMinutes(1)));
                Assert.True(task.TryTransitionTo(RemediationTaskState.Executing, _t0.AddMinutes(2)));
                break;
            case RemediationTaskState.Completed:
                task = MakeTaskIn(RemediationTaskState.Executing);
                Assert.True(task.TryTransitionTo(RemediationTaskState.Completed, _t0.AddMinutes(3)));
                break;
            case RemediationTaskState.Failed:
                task = MakeTaskIn(RemediationTaskState.Executing);
                Assert.True(task.TryTransitionTo(RemediationTaskState.Failed, _t0.AddMinutes(3), "scripted failure"));
                break;
            case RemediationTaskState.NotApplicable:
                task = MakeTaskIn(RemediationTaskState.Executing);
                Assert.True(task.TryTransitionTo(RemediationTaskState.NotApplicable, _t0.AddMinutes(3), "proposal is moot"));
                break;
            case RemediationTaskState.Dismissed:
                Assert.True(task.TryTransitionTo(RemediationTaskState.Dismissed, _t0.AddMinutes(1)));
                break;
        }

        return task;
    }

    private static string? ReasonFor(RemediationTaskState toState)
        => toState is RemediationTaskState.Failed or RemediationTaskState.NotApplicable ? "a reason" : null;

    // ------------------------------------------------------------- creation

    [Fact]
    public void NewTask_StartsProposed_WithVerbatimProposalFields()
    {
        var task = MakeTask();

        Assert.Equal(RemediationTaskState.Proposed, task.State);
        Assert.False(task.IsTerminal);
        Assert.Null(task.AuthorizedAt);
        Assert.Null(task.OutcomeReason);
        Assert.Equal(_t0, task.ProposedAt);
        Assert.Equal(_t0, task.UpdatedAt);
    }

    // ------------------------------------------------------------- valid edges

    [Theory]
    [InlineData(RemediationTaskState.Proposed, RemediationTaskState.Authorized)]
    [InlineData(RemediationTaskState.Proposed, RemediationTaskState.Dismissed)]
    [InlineData(RemediationTaskState.Authorized, RemediationTaskState.Proposed)]
    [InlineData(RemediationTaskState.Authorized, RemediationTaskState.Executing)]
    [InlineData(RemediationTaskState.Executing, RemediationTaskState.Completed)]
    [InlineData(RemediationTaskState.Executing, RemediationTaskState.Failed)]
    [InlineData(RemediationTaskState.Executing, RemediationTaskState.NotApplicable)]
    public void ValidEdge_Commits_AndStampsUpdatedAt(RemediationTaskState from, RemediationTaskState to)
    {
        var task = MakeTaskIn(from);
        var at = _t0.AddMinutes(10);

        Assert.True(task.TryTransitionTo(to, at, ReasonFor(to)));

        Assert.Equal(to, task.State);
        Assert.Equal(at, task.UpdatedAt);
    }

    // ------------------------------------------------------------- invalid edges

    public static TheoryData<RemediationTaskState, RemediationTaskState> InvalidEdges()
    {
        var validEdges = new HashSet<(RemediationTaskState, RemediationTaskState)>
        {
            (RemediationTaskState.Proposed, RemediationTaskState.Authorized),
            (RemediationTaskState.Proposed, RemediationTaskState.Dismissed),
            (RemediationTaskState.Authorized, RemediationTaskState.Proposed),
            (RemediationTaskState.Authorized, RemediationTaskState.Executing),
            (RemediationTaskState.Executing, RemediationTaskState.Completed),
            (RemediationTaskState.Executing, RemediationTaskState.Failed),
            (RemediationTaskState.Executing, RemediationTaskState.NotApplicable),
        };

        var data = new TheoryData<RemediationTaskState, RemediationTaskState>();
        foreach (var from in Enum.GetValues<RemediationTaskState>())
        {
            foreach (var to in Enum.GetValues<RemediationTaskState>())
            {
                if (from != to && !validEdges.Contains((from, to)))
                {
                    data.Add(from, to);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidEdges))]
    public void InvalidEdge_IsRejected_AndLeavesTheTaskUntouched(RemediationTaskState from, RemediationTaskState to)
    {
        var task = MakeTaskIn(from);
        var stateBefore = task.State;
        var updatedBefore = task.UpdatedAt;
        var reasonBefore = task.OutcomeReason;

        Assert.False(task.TryTransitionTo(to, _t0.AddMinutes(10), ReasonFor(to)));

        Assert.Equal(stateBefore, task.State);
        Assert.Equal(updatedBefore, task.UpdatedAt);
        Assert.Equal(reasonBefore, task.OutcomeReason);
    }

    [Theory]
    [InlineData(RemediationTaskState.Completed)]
    [InlineData(RemediationTaskState.Failed)]
    [InlineData(RemediationTaskState.NotApplicable)]
    [InlineData(RemediationTaskState.Dismissed)]
    public void SameStateTransition_IsRejected(RemediationTaskState terminal)
    {
        // Self-transitions are off-edge everywhere, including terminal → same terminal:
        // the first terminal transition already won (idempotence).
        var task = MakeTaskIn(terminal);

        Assert.False(task.TryTransitionTo(terminal, _t0.AddMinutes(20), ReasonFor(terminal)));
    }

    // ------------------------------------------------------------- terminal idempotence

    [Fact]
    public void FirstTerminalTransitionWins_LaterTerminalAttemptIsRejected()
    {
        // Mirrors LintRunState.TryTransitionTo: a duplicate/late terminal event must not
        // overwrite the recorded outcome (data-model.md "first-terminal-wins").
        var task = MakeTaskIn(RemediationTaskState.Executing);

        Assert.True(task.TryTransitionTo(RemediationTaskState.NotApplicable, _t0.AddMinutes(3), "proposal is moot"));
        Assert.False(task.TryTransitionTo(RemediationTaskState.Failed, _t0.AddMinutes(4), "liveness window expired"));

        Assert.Equal(RemediationTaskState.NotApplicable, task.State);
        Assert.Equal("proposal is moot", task.OutcomeReason);
        Assert.True(task.IsTerminal);
    }

    // ------------------------------------------------------------- authorized_at semantics

    [Fact]
    public void Authorize_StampsAuthorizedAt()
    {
        var task = MakeTask();
        var at = _t0.AddMinutes(1);

        Assert.True(task.TryTransitionTo(RemediationTaskState.Authorized, at));

        Assert.Equal(at, task.AuthorizedAt);
    }

    [Fact]
    public void Withdraw_ClearsAuthorizedAt_AndReauthorizingStampsAFreshOne()
    {
        // FR-016: re-authorizing later gets a fresh queue position (authorized_at is the
        // FIFO order authority, FR-017).
        var task = MakeTaskIn(RemediationTaskState.Authorized);

        Assert.True(task.TryTransitionTo(RemediationTaskState.Proposed, _t0.AddMinutes(2)));
        Assert.Null(task.AuthorizedAt);

        var reauthorizedAt = _t0.AddMinutes(5);
        Assert.True(task.TryTransitionTo(RemediationTaskState.Authorized, reauthorizedAt));
        Assert.Equal(reauthorizedAt, task.AuthorizedAt);
    }

    [Fact]
    public void Dispatch_LeavesAuthorizedAtUntouched()
    {
        var task = MakeTaskIn(RemediationTaskState.Authorized);
        var authorizedAt = task.AuthorizedAt;

        Assert.True(task.TryTransitionTo(RemediationTaskState.Executing, _t0.AddMinutes(2)));

        Assert.Equal(authorizedAt, task.AuthorizedAt);
    }

    // ------------------------------------------------------------- outcome_reason invariant

    [Theory]
    [InlineData(RemediationTaskState.Failed)]
    [InlineData(RemediationTaskState.NotApplicable)]
    public void TerminalRequiringReason_WithoutReason_Throws(RemediationTaskState terminal)
    {
        var task = MakeTaskIn(RemediationTaskState.Executing);

        Assert.Throws<ArgumentException>(() => task.TryTransitionTo(terminal, _t0.AddMinutes(3)));
        Assert.Throws<ArgumentException>(() => task.TryTransitionTo(terminal, _t0.AddMinutes(3), "   "));
        Assert.Equal(RemediationTaskState.Executing, task.State);
    }

    [Theory]
    [InlineData(RemediationTaskState.Failed, "liveness window expired")]
    [InlineData(RemediationTaskState.NotApplicable, "the page gained tags after this action was proposed")]
    public void TerminalRequiringReason_WithReason_PersistsItVerbatim(RemediationTaskState terminal, string reason)
    {
        var task = MakeTaskIn(RemediationTaskState.Executing);

        Assert.True(task.TryTransitionTo(terminal, _t0.AddMinutes(3), reason));

        Assert.Equal(reason, task.OutcomeReason);
    }

    [Theory]
    [InlineData(RemediationTaskState.Authorized)]
    [InlineData(RemediationTaskState.Completed)]
    [InlineData(RemediationTaskState.Dismissed)]
    public void ReasonOnAStateThatForbidsIt_Throws(RemediationTaskState to)
    {
        var from = to == RemediationTaskState.Completed ? RemediationTaskState.Executing : RemediationTaskState.Proposed;
        var task = MakeTaskIn(from);

        Assert.Throws<ArgumentException>(() => task.TryTransitionTo(to, _t0.AddMinutes(3), "unexpected reason"));
        Assert.Equal(from, task.State);
    }

    // ------------------------------------------------------------- row projection

    [Fact]
    public void ToRow_And_FromRow_RoundTrip()
    {
        var task = MakeTaskIn(RemediationTaskState.Failed);

        var row = task.ToRow();
        Assert.Equal("failed", row.State);
        Assert.Equal("scripted failure", row.OutcomeReason);

        var rehydrated = RemediationActionTask.FromRow(row);
        Assert.Equal(task.State, rehydrated.State);
        Assert.Equal(task.AuthorizedAt, rehydrated.AuthorizedAt);
        Assert.Equal(task.OutcomeReason, rehydrated.OutcomeReason);
        Assert.Equal(task.UpdatedAt, rehydrated.UpdatedAt);
        Assert.Equal(row, rehydrated.ToRow());
    }

    [Fact]
    public void FromRow_WithUnknownState_Throws()
    {
        var row = new RemediationTaskRow(
            "2026-08-01-remediation-a1b2c3", "2026-08-01-lint-9f8e7d", "t", "d", null,
            "exploded", _t0, null, null, _t0);

        Assert.Throws<ArgumentException>(() => RemediationActionTask.FromRow(row));
    }

    [Fact]
    public void WireFormat_RoundTrips_EveryState()
    {
        foreach (var state in Enum.GetValues<RemediationTaskState>())
        {
            Assert.True(RemediationTaskStates.TryParse(state.ToWireFormat(), out var parsed));
            Assert.Equal(state, parsed);
        }

        Assert.False(RemediationTaskStates.TryParse("Proposed", out _)); // wire format is lowercase snake
    }
}
