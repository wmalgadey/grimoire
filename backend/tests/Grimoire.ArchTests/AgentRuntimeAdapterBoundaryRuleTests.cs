using System.Reflection;
using NetArchTest.Rules;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rules for ADR-011 (C6): the Anthropic SDK must only be referenced
/// from Grimoire.AgentRuntime.Core.Adapters.Anthropic, and orchestration code elsewhere in
/// the assembly must not reference the concrete AnthropicModelClient adapter type directly
/// (Principle I: consume the IModelClient port instead). Together these supersede the
/// ADR-010 C2/C5 containment-table entries for IModelClient's adapter namespace now that
/// the model-client port and its adapter live in the shared Grimoire.AgentRuntime library
/// instead of Grimoire.IngestAgent — re-pointing here (constitution v1.9.0 "Test what we
/// own") rather than leaving the retired IngestAgent-scoped rules in place, which passed
/// only vacuously after the move and so enforced nothing (Principle III).
/// </summary>
public class AgentRuntimeAdapterBoundaryRuleTests
{
    // Loaded by name (not typeof) so this rule compiles regardless of which types
    // Grimoire.AgentRuntime.Core.Adapters.Anthropic currently declares.
    private static Assembly AgentRuntimeAssembly => Assembly.Load("Grimoire.AgentRuntime");

    [Fact]
    public void AnthropicSdk_MustOnlyBeReferencedFrom_AgentRuntimeCoreAdaptersAnthropic()
    {
        var result = Types.InAssembly(AgentRuntimeAssembly)
            .That().HaveDependencyOn("Anthropic")
            .Should().ResideInNamespaceStartingWith("Grimoire.AgentRuntime.Core.Adapters.Anthropic")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C6 (ADR-011): Anthropic SDK types must only be referenced from " +
            "Grimoire.AgentRuntime.Core.Adapters.Anthropic. " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Red/Green probe (T002-equivalent, run 2026-08-11 for the ADR-010→ADR-011 re-point):
    /// a temporary type in Grimoire.AgentRuntime.Host referencing AnthropicModelClient by
    /// name was added, this rule was confirmed to fail naming it, then the type was
    /// removed and the rule was confirmed to pass again — proving the rule actually
    /// detects a violation rather than passing vacuously. Grimoire.AgentRuntime.Composition
    /// is exempt by construction: <c>ModelClientFactory</c> there is ADR-012/ADR-013's
    /// consolidated composition root for model-adapter selection (the direct analogue of
    /// the Hub's Program.cs exemption in <see cref="HexagonalPortsAdapterRuleTests"/>'s C5
    /// rule) and must reference the concrete adapter type to construct it.
    /// </summary>
    [Fact]
    public void AgentOrchestration_MustNotReferenceConcreteAnthropicModelClient()
    {
        var result = Types.InAssembly(AgentRuntimeAssembly)
            .That().ResideInNamespaceStartingWith("Grimoire.AgentRuntime")
            .And().DoNotResideInNamespaceContaining(".Adapters.")
            .And().DoNotResideInNamespaceContaining(".Composition")
            .Should().NotHaveDependencyOn("Grimoire.AgentRuntime.Core.Adapters.Anthropic.AnthropicModelClient")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C5/C6 (ADR-010/ADR-011): namespaces outside .Adapters./.Composition segments " +
            "must not reference the concrete AnthropicModelClient. " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
