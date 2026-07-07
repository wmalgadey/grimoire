using NetArchTest.Rules;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-002: the Ingest agent is invoked exclusively as a
/// standalone child process (Grimoire.Hub.AgentDispatch.IngestAgentDispatcher), never as an
/// in-process library call. Feature 003 substantially grows Hub's submission/task-artifact
/// responsibilities, which is exactly the pressure that could tempt an in-process shortcut.
/// </summary>
public class HubAgentDispatchBoundaryRuleTests
{
    [Fact]
    public void Hub_Must_Not_Depend_On_IngestAgent()
    {
        var result = Types.InAssembly(typeof(Grimoire.Hub.HubTracing).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Grimoire.IngestAgent")
            .GetResult();

        Assert.True(result.IsSuccessful, "Grimoire.Hub must not depend on Grimoire.IngestAgent (ADR-002: child-process dispatch only, never an in-process assembly reference).");
    }
}
