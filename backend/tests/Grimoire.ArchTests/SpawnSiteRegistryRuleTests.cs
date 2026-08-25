using Mono.Cecil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-034 (027-host-stability) R1/R2: every process the
/// harness spawns is a known, non-injectable one. Only <c>AgentProcessHost</c> (ADR-002
/// spawn model, <c>Grimoire.Hub.AgentDispatch.Adapters.AgentProcess</c>) and
/// <c>MarkItDownConverter</c> (ADR-010, <c>Grimoire.Hub.IngestSubmission.Adapters.
/// MarkItDown</c>) may construct or start a <c>System.Diagnostics.Process</c> anywhere in
/// <c>Grimoire.Hub</c> (R1), and no code in the assembly may ever set the shell-parsed
/// <c>ProcessStartInfo.Arguments</c> string property — only <c>ArgumentList.Add</c> (R2).
/// Modeled on <see cref="NonBlockingDispatchRuleTests"/>'s IL-scan idiom, reusing
/// <see cref="ArchScan"/>'s shared call-site walk.
/// </summary>
public class SpawnSiteRegistryRuleTests
{
    private static readonly HashSet<string> _allowedOutermostTypes =
    [
        "Grimoire.Hub.AgentDispatch.Adapters.AgentProcess.AgentProcessHost",
        "Grimoire.Hub.IngestSubmission.Adapters.MarkItDown.MarkItDownConverter",
    ];

    private static readonly string[] _processConstructedTypes =
    [
        "System.Diagnostics.Process",
        "System.Diagnostics.ProcessStartInfo",
    ];

    private static readonly string[] _processStartMethods =
    [
        "System.Diagnostics.Process::Start",
    ];

    private static readonly string[] _shellArgumentsSetters =
    [
        "System.Diagnostics.ProcessStartInfo::set_Arguments",
    ];

    [Fact]
    public void Hub_MustNotConstructOrStartAProcess_OutsideTheSpawnSiteRegistry()
    {
        var assembly = LoadHubAssembly();

        var constructions = ArchScan.FindConstructions(assembly, _processConstructedTypes);
        var starts = ArchScan.FindCalls(assembly, _processStartMethods);

        var violations = constructions.Concat(starts)
            .Where(site => !_allowedOutermostTypes.Contains(site.TopLevelTypeFullName))
            .Select(site => site.Description)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "ADR-034 R1: only AgentProcessHost and MarkItDownConverter may construct or " +
            "start a System.Diagnostics.Process anywhere in Grimoire.Hub. Violations:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void Hub_MustNeverUseShellParsedProcessArguments_OnlyArgumentList()
    {
        var assembly = LoadHubAssembly();

        var violations = ArchScan.FindCalls(assembly, _shellArgumentsSetters)
            .Select(site => site.Description)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "ADR-034 R2: no code in Grimoire.Hub may set ProcessStartInfo.Arguments (the " +
            "shell-parsed string property) — every spawn site must construct its argument " +
            "list via ProcessStartInfo.ArgumentList.Add instead. Violations:\n" +
            string.Join("\n", violations));
    }

    private static AssemblyDefinition LoadHubAssembly()
    {
        var assemblyPath = typeof(Grimoire.Hub.HubMetrics).Assembly.Location;
        return AssemblyDefinition.ReadAssembly(assemblyPath);
    }
}
