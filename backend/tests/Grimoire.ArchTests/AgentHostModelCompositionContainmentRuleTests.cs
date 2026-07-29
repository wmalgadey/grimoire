using Mono.Cecil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural rule D2 for ADR-013 (feature 010): model-adapter composition containment.
///
/// Agent host assemblies (Grimoire.IngestAgent, Grimoire.QueryAgent, and by naming
/// pattern any future Grimoire.*Agent executable) must not construct the concrete
/// model-client adapters (AnthropicModelClient / ReplayModelClient /
/// TurnCaptureModelClient) directly. The only permitted construction site is
/// Grimoire.AgentRuntime.Composition.ModelClientFactory — the single implementation of
/// ADR-012's replay/capture/live selection (env-var contract and fail-fast semantics
/// unchanged), invoked from each host's composition root with the profile's per-agent
/// model/base-url env-var names (ADR-004).
///
/// Legacy baseline (ratchet, REMOVE-ONLY): the two pre-consolidation CreateModelClient
/// implementations (top-level-statement Program classes). Emptied by the US1 host
/// switches (T017/T018, T020/T021); the mechanism is dismantled with the feature
/// (T050 verifies). Proven live by a Red/Green probe (T006).
/// </summary>
public class AgentHostModelCompositionContainmentRuleTests
{
    private static readonly string[] _modelClientAdapterTypes =
    [
        "Grimoire.AgentRuntime.Core.Adapters.Anthropic.AnthropicModelClient",
        "Grimoire.AgentRuntime.Core.Adapters.Replay.ReplayModelClient",
        "Grimoire.AgentRuntime.Core.Adapters.Replay.TurnCaptureModelClient",
    ];

    /// <summary>
    /// Legacy pre-consolidation composition sites as "AssemblyName::TopLevelTypeFullName".
    /// REMOVE-ONLY.
    /// </summary>
    internal static readonly List<string> LegacyBaseline =
    [
        "Grimoire.QueryAgent::Program",
    ];

    [Fact]
    public void AgentHostAssemblies_MustNotConstructModelClientAdaptersDirectly()
    {
        var violations = new List<string>();

        foreach (var assemblyPath in ArchScan.AgentHostAssemblyPaths())
        {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

            foreach (var construction in ArchScan.FindConstructions(assembly, _modelClientAdapterTypes))
            {
                if (LegacyBaseline.Contains($"{assembly.Name.Name}::{construction.TopLevelTypeFullName}"))
                    continue;

                violations.Add($"{assembly.Name.Name}: {construction.Description}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "D2 (ADR-013): agent host assemblies must not construct model-client adapters directly — " +
            "the ADR-012 replay/capture/live selection exists exactly once in " +
            "Grimoire.AgentRuntime.Composition.ModelClientFactory (invoked from the host's " +
            "composition root). Violations:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void AgentRuntime_ModelClientConstruction_OnlyInAdaptersOrModelClientFactory()
    {
        var assemblyPath = System.Reflection.Assembly.Load("Grimoire.AgentRuntime").Location;
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        var violations = ArchScan.FindConstructions(assembly, _modelClientAdapterTypes)
            .Where(v => !v.EffectiveNamespace.StartsWith("Grimoire.AgentRuntime.Core.Adapters.", StringComparison.Ordinal))
            .Where(v => v.TopLevelTypeFullName != "Grimoire.AgentRuntime.Composition.ModelClientFactory")
            .Select(v => v.Description)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "D2 (ADR-013): within the platform library, concrete model-client adapters may be " +
            "constructed only inside their own adapter namespaces or by " +
            "Grimoire.AgentRuntime.Composition.ModelClientFactory. Violations:\n" +
            string.Join("\n", violations));
    }
}
