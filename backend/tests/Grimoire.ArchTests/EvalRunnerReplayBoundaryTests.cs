using System.Reflection;
using NetArchTest.Rules;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rules for ADR-011 (feature 009-agent-eval-replay): the
/// recorded-replay adapters stay confined to their ADR-010-style adapter namespace,
/// and the standalone eval runner can reach the model only through the spawned agent
/// process or the <c>IModelClient</c> port — never via an LLM SDK or a concrete
/// adapter type. Each rule was proven live with a Red/Green probe when introduced
/// (temporary violating code, test observed failing, code deleted).
/// </summary>
public class EvalRunnerReplayBoundaryTests
{
    private static Assembly IngestAgentAssembly => typeof(Grimoire.IngestAgent.AgentCliOptions).Assembly;
    private static Assembly EvalRunnerAssembly => typeof(Grimoire.EvalRunner.EvalRunnerAssemblyMarker).Assembly;

    // ---- C6a: the Replay adapter namespace references no LLM SDK package ----

    [Fact]
    public void ReplayAdapterNamespace_MustNotDependOn_AnthropicSdk()
    {
        var result = Types.InAssembly(IngestAgentAssembly)
            .That().ResideInNamespaceStartingWith("Grimoire.IngestAgent.AgentCore.Adapters.Replay")
            .ShouldNot().HaveDependencyOn("Anthropic")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C6 (ADR-011): Grimoire.IngestAgent.AgentCore.Adapters.Replay must not reference " +
            "the Anthropic SDK — replay is a pure port implementation. " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- C6b: no namespace outside an .Adapters. segment references the concrete
    // replay adapter types (C5 extension to the new types; composition root exempt
    // by construction — Program.cs compiles into the global namespace, outside the
    // "Grimoire.IngestAgent" prefix filter) ----

    [Fact]
    public void AgentOrchestration_MustNotReferenceConcreteReplayAdapters()
    {
        var result = Types.InAssembly(IngestAgentAssembly)
            .That().ResideInNamespaceStartingWith("Grimoire.IngestAgent")
            .And().DoNotResideInNamespaceContaining(".Adapters.")
            .Should().NotHaveDependencyOnAny(
            [
                "Grimoire.IngestAgent.AgentCore.Adapters.Replay.ReplayModelClient",
                "Grimoire.IngestAgent.AgentCore.Adapters.Replay.TurnCaptureModelClient",
            ])
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C6 (ADR-011): namespaces outside an .Adapters. segment must not reference the " +
            "concrete ReplayModelClient/TurnCaptureModelClient (composition root exempt). " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- C7: the eval runner references no LLM SDK and no concrete adapter type ----

    [Fact]
    public void EvalRunner_MustNotDependOn_AnthropicSdk()
    {
        var result = Types.InAssembly(EvalRunnerAssembly)
            .ShouldNot().HaveDependencyOn("Anthropic")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C7 (ADR-011): Grimoire.EvalRunner must not reference the Anthropic SDK — its only " +
            "paths to a model are the spawned agent process and the IModelClient port. " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void EvalRunner_MustNotReferenceConcreteAdapterTypes()
    {
        var result = Types.InAssembly(EvalRunnerAssembly)
            .ShouldNot().HaveDependencyOnAny(
            [
                "Grimoire.IngestAgent.AgentCore.Adapters.Anthropic.AnthropicModelClient",
                "Grimoire.IngestAgent.AgentCore.Adapters.Replay.ReplayModelClient",
                "Grimoire.IngestAgent.AgentCore.Adapters.Replay.TurnCaptureModelClient",
            ])
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C7 (ADR-011): Grimoire.EvalRunner must not reference concrete .Adapters. types " +
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
            "C8 (ADR-011): System.Diagnostics.Process usage in Grimoire.EvalRunner must be " +
            "confined to Grimoire.EvalRunner.Workspace (mirror of ADR-010 C4). " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
