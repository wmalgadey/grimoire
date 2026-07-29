using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural rule D1 for ADR-013 (feature 010): telemetry-bootstrap containment.
///
/// In agent host assemblies (Grimoire.IngestAgent, Grimoire.QueryAgent, and — by
/// naming pattern — any future Grimoire.*Agent executable), OpenTelemetry SDK
/// provider-construction APIs (Sdk.CreateTracerProviderBuilder,
/// Sdk.CreateMeterProviderBuilder, OTel LoggerFactory wiring) are forbidden: the one
/// permitted implementation site is Grimoire.AgentRuntime.Telemetry
/// (AgentTelemetryBootstrap), which takes the frozen per-agent identities as inputs.
/// This makes the 68+63-line telemetry-bootstrap duplication structurally
/// unrepeatable for agent three (SC-001).
///
/// Legacy baseline (ratchet, REMOVE-ONLY): the two pre-consolidation host bootstraps.
/// Emptied by the US1 host switches (T018/T021); the mechanism is dismantled with the
/// feature (T050 verifies). Proven live by a Red/Green probe (T004).
/// </summary>
public class AgentHostTelemetryContainmentRuleTests
{
    // Declaring-type::method prefixes that construct OTel providers / wire the OTel
    // logger pipeline.
    private static readonly string[] _providerConstructionApis =
    [
        "OpenTelemetry.Sdk::CreateTracerProviderBuilder",
        "OpenTelemetry.Sdk::CreateMeterProviderBuilder",
        "OpenTelemetry.Logs.OpenTelemetryLoggingExtensions::AddOpenTelemetry",
    ];

    /// <summary>Legacy pre-consolidation bootstraps (full type names). REMOVE-ONLY.</summary>
    internal static readonly List<string> LegacyBaseline =
    [
        "Grimoire.QueryAgent.QueryAgentTelemetryBootstrap",
    ];

    [Fact]
    public void AgentHostAssemblies_MustNotConstructTelemetryProviders()
    {
        var violations = new List<string>();

        foreach (var assemblyPath in ArchScan.AgentHostAssemblyPaths())
        {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

            foreach (var violation in ArchScan.FindCalls(assembly, _providerConstructionApis))
            {
                if (LegacyBaseline.Contains(violation.TopLevelTypeFullName))
                    continue;

                violations.Add($"{assembly.Name.Name}: {violation.Description}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "D1 (ADR-013): agent host assemblies must not construct OpenTelemetry providers — " +
            "the telemetry bootstrap lives exactly once in Grimoire.AgentRuntime.Telemetry " +
            "(AgentTelemetryBootstrap), parameterized by the profile's frozen identities. Violations:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void AgentRuntime_TelemetryProviderConstruction_OnlyInTelemetryNamespace()
    {
        var assemblyPath = System.Reflection.Assembly.Load("Grimoire.AgentRuntime").Location;
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        var violations = ArchScan.FindCalls(assembly, _providerConstructionApis)
            .Where(v => !v.EffectiveNamespace.StartsWith("Grimoire.AgentRuntime.Telemetry", StringComparison.Ordinal))
            .Select(v => v.Description)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "D1 (ADR-013): within the platform library, OpenTelemetry provider construction is " +
            "permitted only in Grimoire.AgentRuntime.Telemetry. Violations:\n" +
            string.Join("\n", violations));
    }
}
