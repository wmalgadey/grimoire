using System.Reflection;
using NetArchTest.Rules;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rules for ADR-010 (Constitution Principle I, hexagonal ports &amp;
/// adapters) — contracts/ports-and-adapters.md C1-C5 plus the P1-P4 port-presence and
/// port-ownership checks. This codebase is a remediation target for feature
/// 006-hexagonal-arch-tasks-ui: C2-C5 and the port-presence checks have genuine active
/// violations when this file is first introduced (nothing yet resides under an
/// <c>.Adapters.</c> namespace, the two new ports do not exist, and
/// <c>SubmissionService</c> depends on the concrete <c>AgentProcessHost</c>). Only C1 has
/// no active violation. User Story 1 turns every rule Green through a move-only,
/// interface-extraction restructuring; T018 then re-proves each rule live via a
/// synthetic Red/Green probe.
/// </summary>
public class HexagonalPortsAdapterRuleTests
{
    private static Assembly HubAssembly => typeof(Grimoire.Hub.HubMetrics).Assembly;
    private static Assembly IngestAgentAssembly => typeof(Grimoire.IngestAgent.AgentCliOptions).Assembly;
    private static Assembly AgentEvalsAssembly => typeof(Grimoire.AgentEvals.EvalGate).Assembly;

    // ---- C2 (007-eval-tests-nim-endpoint): the eval harness must reach the Anthropic
    // Messages API only through the ADR-010 IModelClient port (AnthropicModelClient),
    // never by depending on the Anthropic SDK directly — this is the architectural premise
    // the eval-provider-resolution design in research.md D1/D2 relies on.

    [Fact]
    public void AgentEvals_MustNotDependOn_AnthropicSdk()
    {
        var result = Types.InAssembly(AgentEvalsAssembly)
            .ShouldNot()
            .HaveDependencyOn("Anthropic")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C2 (ADR-010, extended by 007-eval-tests-nim-endpoint): Grimoire.AgentEvals must not " +
            "depend on the Anthropic SDK directly; it must reach model providers only through " +
            "the IModelClient port. Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- C1: Microsoft.Data.Sqlite confined to the designated persistence adapter ----
    // No active violation (OperationalStateRepository already owns all Sqlite usage);
    // proven live by T001's Red/Green probe (temporary violating class, deleted after).

    [Fact]
    public void Sqlite_MustOnlyBeReferencedFrom_OperationalState()
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

    // ---- C2: Anthropic SDK confined to the Anthropic adapter namespace ----

    [Fact]
    public void AnthropicSdk_MustOnlyBeReferencedFrom_AgentCoreAdaptersAnthropic()
    {
        var result = Types.InAssembly(IngestAgentAssembly)
            .That().HaveDependencyOn("Anthropic")
            .Should().ResideInNamespaceStartingWith("Grimoire.IngestAgent.AgentCore.Adapters.Anthropic")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C2 (ADR-010): Anthropic SDK types must only be referenced from " +
            "Grimoire.IngestAgent.AgentCore.Adapters.Anthropic. " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- C3: outbound HTTP client usage in the Hub confined to the HttpFetch adapter ----
    // Composition root (Program.cs, empty namespace) and TelemetryExtensions (framework
    // wiring, no HttpClient usage of its own) are exempt by construction: neither resides
    // under a "Grimoire.Hub.IngestSubmission" prefix that this rule scopes to... instead
    // the rule scopes to the whole Hub assembly and only *requires* the adapter namespace
    // for types that actually depend on System.Net.Http, so anything that never touches
    // HttpClient (including the composition root and telemetry wiring) never triggers it.

    [Fact]
    public void HubHttpClientUsage_MustOnlyBeReferencedFrom_IngestSubmissionAdaptersHttpFetch()
    {
        var result = Types.InAssembly(HubAssembly)
            .That().HaveDependencyOn("System.Net.Http")
            .Should().ResideInNamespaceStartingWith("Grimoire.Hub.IngestSubmission.Adapters.HttpFetch")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C3 (ADR-010): System.Net.Http usage in the Hub must be confined to " +
            "Grimoire.Hub.IngestSubmission.Adapters.HttpFetch (composition root/telemetry " +
            "wiring exempt). Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- C4: process spawning in the Hub confined to the two designated adapters ----

    [Fact]
    public void HubProcessSpawning_MustOnlyBeReferencedFrom_DesignatedAdapters()
    {
        var result = Types.InAssembly(HubAssembly)
            .That().HaveDependencyOn("System.Diagnostics.Process")
            .Should().ResideInNamespaceStartingWith("Grimoire.Hub.AgentDispatch.Adapters.AgentProcess")
            .Or().ResideInNamespaceStartingWith("Grimoire.Hub.IngestSubmission.Adapters.MarkItDown")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C4 (ADR-010): System.Diagnostics.Process usage in the Hub must be confined to " +
            "Grimoire.Hub.AgentDispatch.Adapters.AgentProcess and " +
            "Grimoire.Hub.IngestSubmission.Adapters.MarkItDown. " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- C5: orchestration namespaces must not reference concrete adapter types ----
    // Composition roots (Program.cs) compile to a type in the empty/global namespace, so
    // they fall outside the "Grimoire.Hub" / "Grimoire.IngestAgent" prefix filter below
    // and are exempt by construction — no explicit carve-out needed.
    //
    // T017: the moves in T007-T010 landed the adapters under their ADR-010 ".Adapters."
    // namespace, so the forbidden-type list below now names those final locations —
    // this rule enforces C5 permanently, not just during the migration.

    [Fact]
    public void HubOrchestration_MustNotReferenceConcreteAdapterTypes()
    {
        var result = Types.InAssembly(HubAssembly)
            .That().ResideInNamespaceStartingWith("Grimoire.Hub")
            .And().DoNotResideInNamespaceContaining(".Adapters.")
            .Should().NotHaveDependencyOnAny(
            [
                "Grimoire.Hub.AgentDispatch.Adapters.AgentProcess.AgentProcessHost",
                "Grimoire.Hub.IngestSubmission.Adapters.MarkItDown.MarkItDownConverter",
                "Grimoire.Hub.IngestSubmission.Adapters.HttpFetch.UrlContentFetcher",
            ])
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C5 (ADR-010): namespaces outside an .Adapters. segment must not reference " +
            "concrete Hub adapter types (composition root exempt). " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void AgentOrchestration_MustNotReferenceConcreteAnthropicModelClient()
    {
        var result = Types.InAssembly(IngestAgentAssembly)
            .That().ResideInNamespaceStartingWith("Grimoire.IngestAgent")
            .And().DoNotResideInNamespaceContaining(".Adapters.")
            .Should().NotHaveDependencyOn("Grimoire.IngestAgent.AgentCore.Adapters.Anthropic.AnthropicModelClient")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "C5 (ADR-010): namespaces outside an .Adapters. segment must not reference " +
            "the concrete AnthropicModelClient (composition root exempt). " +
            "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- Port presence & ownership (P1-P4): ports exist where the ADR says they must,
    // and each production adapter implements the port it is paired with. Looked up by
    // string type name (not typeof) so this file compiles both before and after the
    // ports/adapters exist at their ADR-010 locations.

    [Fact]
    public void MarkdownConverterPort_ExistsInIngestSubmission()
    {
        var portType = HubAssembly.GetType("Grimoire.Hub.IngestSubmission.IMarkdownConverter");

        Assert.True(portType is { IsInterface: true },
            "P2 (ADR-010): IMarkdownConverter must exist as an interface in Grimoire.Hub.IngestSubmission.");
    }

    [Fact]
    public void UrlContentFetcherPort_ExistsInIngestSubmission()
    {
        var portType = HubAssembly.GetType("Grimoire.Hub.IngestSubmission.IUrlContentFetcher");

        Assert.True(portType is { IsInterface: true },
            "P3 (ADR-010): IUrlContentFetcher must exist as an interface in Grimoire.Hub.IngestSubmission.");
    }

    [Theory]
    [InlineData("Grimoire.Hub.AgentDispatch.Adapters.AgentProcess.AgentProcessHost", "Grimoire.Hub.AgentDispatch.IAgentProcessLauncher")]
    [InlineData("Grimoire.Hub.IngestSubmission.Adapters.MarkItDown.MarkItDownConverter", "Grimoire.Hub.IngestSubmission.IMarkdownConverter")]
    [InlineData("Grimoire.Hub.IngestSubmission.Adapters.HttpFetch.UrlContentFetcher", "Grimoire.Hub.IngestSubmission.IUrlContentFetcher")]
    public void HubAdapter_ImplementsItsPort(string adapterTypeName, string portTypeName)
    {
        var adapterType = HubAssembly.GetType(adapterTypeName);
        Assert.True(adapterType is not null, $"Adapter type '{adapterTypeName}' not found at its ADR-010 namespace in Grimoire.Hub.");

        var portType = HubAssembly.GetType(portTypeName);
        Assert.True(portType is not null, $"Port type '{portTypeName}' not found in Grimoire.Hub.");

        Assert.True(portType!.IsAssignableFrom(adapterType),
            $"ADR-010: '{adapterTypeName}' must implement '{portTypeName}'.");
    }

    [Fact]
    public void AnthropicModelClient_ImplementsIModelClient()
    {
        var adapterType = IngestAgentAssembly.GetType("Grimoire.IngestAgent.AgentCore.Adapters.Anthropic.AnthropicModelClient");
        Assert.True(adapterType is not null, "Adapter type 'AnthropicModelClient' not found at its ADR-010 namespace.");

        var portType = typeof(Grimoire.IngestAgent.AgentCore.IModelClient);
        Assert.True(portType.IsAssignableFrom(adapterType),
            "P4 (ADR-010): AnthropicModelClient must implement IModelClient.");
    }
}
