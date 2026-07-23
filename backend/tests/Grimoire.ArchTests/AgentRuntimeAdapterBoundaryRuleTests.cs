using System.Reflection;
using NetArchTest.Rules;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-011 (C6): the Anthropic SDK must only be referenced
/// from Grimoire.AgentRuntime.Core.Adapters.Anthropic. This supersedes the ADR-010 C2
/// rule's containment table entry for IModelClient's adapter namespace now that the
/// model-client port and its adapter live in the shared Grimoire.AgentRuntime library
/// instead of Grimoire.IngestAgent. Proven live by a Red/Green probe (T002): a
/// temporary violating type is added to Grimoire.AgentRuntime.Core, the rule is
/// confirmed to fail, then the type is removed and the rule is confirmed to pass again.
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
}
