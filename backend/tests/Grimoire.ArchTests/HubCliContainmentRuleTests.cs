using NetArchTest.Rules;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural containment rule C9 for ADR-020 (feature 018-hub-cli-commands): any type in
/// the Grimoire.Hub assembly that depends on Spectre.Console or Spectre.Console.Cli must
/// reside in the new cross-agent <c>Grimoire.Hub.Cli</c> namespace (the CLI command
/// surface — catalog, command classes, help provider, status renderer, exit-code mapping)
/// or be the composition root (<c>Program.cs</c>, the single dispatch gate that decides
/// between <c>CommandApp.RunAsync</c> and the web-host path). Spectre.Console must not leak
/// into orchestration/coordinator namespaces the way a hand-rolled parser previously lived
/// nowhere in particular.
///
/// NetArchTest cannot express the "OR is the composition root" exemption as a single
/// <c>ResideInNamespaceStartingWith(...).Or()...</c> predicate chain: top-level-statement
/// <c>Program</c> types compile to the empty/global namespace, and
/// <c>ResideInNamespaceStartingWith("")</c> matches every type's <c>FullName</c> (which
/// always starts with the empty string), gutting the rule entirely rather than isolating
/// the composition root. Instead the rule runs the ordinary
/// <c>Types.InAssembly(...).That()...Should()...GetResult()</c> shape (same as the other
/// C-rules in <see cref="HexagonalPortsAdapterRuleTests"/> and
/// <see cref="RemediationTasksContainmentRuleTests"/>) and then post-filters
/// <c>TestResult.FailingTypes</c> for the composition-root exemption via
/// <c>type.Namespace is null or { Length: 0 }</c> — the same idiom
/// <see cref="AgentArtifactNamingRuleTests.HubNamespaces_MustFollowTheOwnershipMap"/> uses
/// to exempt the composition root in Part 2 of N1.
///
/// At Phase 0 (018-hub-cli-commands T001) Grimoire.Hub does not yet reference
/// Spectre.Console/Spectre.Console.Cli at all (the package arrives in T003), so this rule
/// passes vacuously — proven live by a Red/Green probe: a temporary
/// <c>PackageReference</c> to Spectre.Console.Cli plus a deliberately violating class
/// outside <c>Grimoire.Hub.Cli</c> that references <c>Spectre.Console.AnsiConsole</c>,
/// confirmed to fail the rule below, then both the probe class and the temporary package
/// reference deleted and the rule confirmed green again (result documented in the Phase 0
/// commit message).
/// </summary>
public class HubCliContainmentRuleTests
{
    private static System.Reflection.Assembly HubAssembly => typeof(Grimoire.Hub.HubMetrics).Assembly;

    private const string HubCliNamespace = "Grimoire.Hub.Cli";

    // ---- C9: Spectre.Console / Spectre.Console.Cli confined to Grimoire.Hub.Cli* + the
    // composition root (Program.cs dispatch gate) ----

    [Fact]
    public void SpectreConsole_MustOnlyBeReferencedFrom_HubCliOrCompositionRoot()
    {
        var result = Types.InAssembly(HubAssembly)
            .That().HaveDependencyOnAny("Spectre.Console", "Spectre.Console.Cli")
            .Should().ResideInNamespaceStartingWith(HubCliNamespace)
            .GetResult();

        // Composition-root exemption: top-level-statement Program types compile to the
        // empty/global namespace and are the one place outside Grimoire.Hub.Cli permitted
        // to reference Spectre.Console.Cli (the CommandApp.RunAsync dispatch gate, ADR-020).
        var violations = (result.FailingTypes ?? [])
            .Where(type => !string.IsNullOrEmpty(type.Namespace))
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        Assert.True(violations.Count == 0,
            "C9 (ADR-020): Spectre.Console/Spectre.Console.Cli types must only be " +
            "referenced from Grimoire.Hub.Cli (the CLI command surface) or the composition " +
            "root (Program.cs dispatch gate). Violations: " + string.Join(", ", violations));
    }
}
