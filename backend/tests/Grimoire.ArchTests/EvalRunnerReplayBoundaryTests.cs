using System.Reflection;
using NetArchTest.Rules;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rules for ADR-012 (feature 009-agent-eval-replay): the
/// recorded-replay adapters stay confined to their ADR-011-style adapter namespace
/// (relocated from Grimoire.IngestAgent to the shared Grimoire.AgentRuntime by T094,
/// 008-query-agent, so Grimoire.QueryAgent's own composition root can reuse them), and
/// the standalone eval runner can reach the model only through the spawned agent
/// process or the <c>IModelClient</c> port — never via an LLM SDK or a concrete
/// adapter type. Each rule was proven live with a Red/Green probe when introduced
/// (temporary violating code, test observed failing, code deleted).
/// </summary>
public class EvalRunnerReplayBoundaryTests
{
    // Loaded by name (not typeof) so these rules compile regardless of which types each
    // assembly currently declares.
    private static Assembly AgentRuntimeAssembly => Assembly.Load("Grimoire.AgentRuntime");
    private static Assembly IngestAgentAssembly => typeof(Grimoire.IngestAgent.IngestCliOptions).Assembly;
    private static Assembly QueryAgentAssembly => typeof(Grimoire.QueryAgent.QueryCliOptions).Assembly;
    private static Assembly EvalRunnerAssembly => typeof(Grimoire.EvalRunner.EvalRunnerAssemblyMarker).Assembly;

    // ---- C6a: the Replay adapter namespace references no LLM SDK package ----

    [Fact]
    public void ReplayAdapterNamespace_MustNotDependOn_AnthropicSdk()
    {
        var result = Types.InAssembly(AgentRuntimeAssembly)
            .That().ResideInNamespaceStartingWith("Grimoire.AgentRuntime.Core.Adapters.Replay")
            .ShouldNot().HaveDependencyOn("Anthropic")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C6 (ADR-012): Grimoire.AgentRuntime.Core.Adapters.Replay must not reference " +
            "the Anthropic SDK — replay is a pure port implementation. " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- C6b: no namespace outside an .Adapters. segment references the concrete
    // replay adapter types (composition roots exempt by construction — each agent's
    // Program.cs compiles into the global namespace, outside these prefix filters).
    // ADR-013 (feature 010) moves the composition-root selection into the single shared
    // ModelClientFactory (rule D2 pins it as the only permitted construction site), so
    // that one type joins the exemption — the boundary moved, the rule moved with it
    // (FR-010); everything else in Grimoire.AgentRuntime stays forbidden. ----

    [Fact]
    public void AgentRuntimeCore_MustNotReferenceConcreteReplayAdapters_OutsideAdaptersNamespace()
    {
        var result = Types.InAssembly(AgentRuntimeAssembly)
            .That().ResideInNamespaceStartingWith("Grimoire.AgentRuntime")
            .And().DoNotResideInNamespaceContaining(".Adapters.")
            .And().DoNotHaveNameMatching("^ModelClientFactory$")
            .Should().NotHaveDependencyOnAny(
            [
                "Grimoire.AgentRuntime.Core.Adapters.Replay.ReplayModelClient",
                "Grimoire.AgentRuntime.Core.Adapters.Replay.TurnCaptureModelClient",
            ])
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C6 (ADR-012): Grimoire.AgentRuntime namespaces outside an .Adapters. segment must not " +
            "reference the concrete ReplayModelClient/TurnCaptureModelClient. " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void IngestAgentOrchestration_MustNotReferenceConcreteReplayAdapters()
    {
        var result = Types.InAssembly(IngestAgentAssembly)
            .That().ResideInNamespaceStartingWith("Grimoire.IngestAgent")
            .And().DoNotResideInNamespaceContaining(".Adapters.")
            .Should().NotHaveDependencyOnAny(
            [
                "Grimoire.AgentRuntime.Core.Adapters.Replay.ReplayModelClient",
                "Grimoire.AgentRuntime.Core.Adapters.Replay.TurnCaptureModelClient",
            ])
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C6 (ADR-012): Grimoire.IngestAgent namespaces outside an .Adapters. segment must not " +
            "reference the concrete ReplayModelClient/TurnCaptureModelClient (composition root " +
            "exempt). Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void QueryAgentOrchestration_MustNotReferenceConcreteReplayAdapters()
    {
        var result = Types.InAssembly(QueryAgentAssembly)
            .That().ResideInNamespaceStartingWith("Grimoire.QueryAgent")
            .And().DoNotResideInNamespaceContaining(".Adapters.")
            .Should().NotHaveDependencyOnAny(
            [
                "Grimoire.AgentRuntime.Core.Adapters.Replay.ReplayModelClient",
                "Grimoire.AgentRuntime.Core.Adapters.Replay.TurnCaptureModelClient",
            ])
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C6 (ADR-012, T095): Grimoire.QueryAgent namespaces outside an .Adapters. segment must " +
            "not reference the concrete ReplayModelClient/TurnCaptureModelClient (composition root " +
            "exempt). Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- C7: the eval runner references no LLM SDK and no concrete adapter type ----

    [Fact]
    public void EvalRunner_MustNotDependOn_AnthropicSdk()
    {
        var result = Types.InAssembly(EvalRunnerAssembly)
            .ShouldNot().HaveDependencyOn("Anthropic")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C7 (ADR-012): Grimoire.EvalRunner must not reference the Anthropic SDK — its only " +
            "paths to a model are the spawned agent process and the IModelClient port. " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // Composition root exempt by construction: Program.cs compiles into the global
    // namespace, outside the "Grimoire.EvalRunner" prefix filter — it is the single
    // place allowed to construct the capture-time judge adapter (ADR-012).
    [Fact]
    public void EvalRunner_MustNotReferenceConcreteAdapterTypes()
    {
        var result = Types.InAssembly(EvalRunnerAssembly)
            .That().ResideInNamespaceStartingWith("Grimoire.EvalRunner")
            .ShouldNot().HaveDependencyOnAny(
            [
                "Grimoire.AgentRuntime.Core.Adapters.Anthropic.AnthropicModelClient",
                "Grimoire.AgentRuntime.Core.Adapters.Replay.ReplayModelClient",
                "Grimoire.AgentRuntime.Core.Adapters.Replay.TurnCaptureModelClient",
            ])
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C7 (ADR-012): Grimoire.EvalRunner must not reference concrete .Adapters. types " +
            "from any assembly. Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- C8: process spawning in the eval runner confined to its Workspace namespace ----

    [Fact]
    public void EvalRunnerProcessSpawning_MustOnlyBeReferencedFrom_Workspace()
    {
        var result = Types.InAssembly(EvalRunnerAssembly)
            .That().HaveDependencyOn("System.Diagnostics.Process")
            .Should().ResideInNamespaceStartingWith("Grimoire.EvalRunner.Workspace")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C8 (ADR-012): System.Diagnostics.Process usage in Grimoire.EvalRunner must be " +
            "confined to Grimoire.EvalRunner.Workspace (mirror of ADR-010 C4). " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
