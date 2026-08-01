using NetArchTest.Rules;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural containment rule for ADR-010, extended by 015-lint-board-parity (T001):
/// the new <c>Grimoire.Hub.RemediationTasks</c> namespace holds the remediation task
/// entity, coordinator, record store, and endpoints — orchestration plus port-exempt
/// persistence (Constitution Principle I), never infrastructure. Like the other Hub
/// feature namespaces (<c>LintDispatch</c>, <c>QueryConversations</c>, ...), it must not
/// reference infrastructure packages directly: SQLite stays confined to
/// <c>Grimoire.Hub.OperationalState</c> (C1), outbound HTTP to the HttpFetch adapter
/// (C3), and process spawning to the designated adapters (C4).
///
/// At Phase 0 (015-lint-board-parity T001) the RemediationTasks namespace does not exist
/// yet, so the namespace-scoped rule passes vacuously — proven live by a Red/Green probe
/// (a temporary class in <c>Grimoire.Hub.RemediationTasks</c> importing
/// <c>Microsoft.Data.Sqlite</c>, confirmed to fail both rules below, then deleted;
/// result documented in the Phase 0 commit message).
/// </summary>
public class RemediationTasksContainmentRuleTests
{
    private static System.Reflection.Assembly HubAssembly => typeof(Grimoire.Hub.HubMetrics).Assembly;

    private const string RemediationTasksNamespace = "Grimoire.Hub.RemediationTasks";

    [Fact]
    public void RemediationTasks_MustNotReferenceInfrastructurePackages()
    {
        var result = Types.InAssembly(HubAssembly)
            .That().ResideInNamespaceStartingWith(RemediationTasksNamespace)
            .ShouldNot().HaveDependencyOnAny(
            [
                // Persistence driver: confined to Grimoire.Hub.OperationalState (C1) —
                // remediation rows go through OperationalStateRepository, never raw SQLite.
                "Microsoft.Data.Sqlite",
                // Outbound HTTP: confined to the HttpFetch adapter namespace (C3).
                "System.Net.Http",
                // Process spawning: confined to the AgentProcess/MarkItDown adapters (C4) —
                // remediation execution reaches the child process only via the
                // IAgentProcessLauncher port (see RemediationExecutionDispatchRuleTests).
                "System.Diagnostics.Process",
            ])
            .GetResult();

        Assert.True(result.IsSuccessful,
            "ADR-010 (015-lint-board-parity T001): Grimoire.Hub.RemediationTasks must not " +
            "reference infrastructure packages (Microsoft.Data.Sqlite, System.Net.Http, " +
            "System.Diagnostics.Process) — persistence goes through " +
            "Grimoire.Hub.OperationalState, process spawning through the " +
            "IAgentProcessLauncher port. Violations: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Re-asserts C1 from this feature's perspective (T001 rule (b)): introducing the
    /// remediation_tasks table must not tempt any namespace — RemediationTasks included —
    /// into referencing SQLite outside the designated persistence adapter. Same rule as
    /// <see cref="HexagonalPortsAdapterRuleTests.Sqlite_MustOnlyBeReferencedFrom_OperationalState"/>,
    /// restated here so the T001 Red/Green probe fails self-contained within this class.
    /// </summary>
    [Fact]
    public void Sqlite_StaysConfinedTo_OperationalState()
    {
        var result = Types.InAssembly(HubAssembly)
            .That().HaveDependencyOn("Microsoft.Data.Sqlite")
            .Should().ResideInNamespaceStartingWith("Grimoire.Hub.OperationalState")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C1 (ADR-010): Microsoft.Data.Sqlite types must only be referenced from " +
            "Grimoire.Hub.OperationalState (the designated persistence adapter namespace). " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
