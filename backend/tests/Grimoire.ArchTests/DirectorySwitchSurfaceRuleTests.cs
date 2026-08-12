using System.Reflection;
using Grimoire.Hub.Cli;
using Spectre.Console.Cli;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule R1 for ADR-022, amended by ADR-024 rule M1: the hub's
/// directory-option surface is structurally capped at exactly four command-line switches
/// (<c>--data-dir</c>, <c>--agent-dir</c>, <c>--wiki-dir</c>, <c>--memory-dir</c>), and
/// <see cref="HubPathSettings"/> declares exactly one <c>[CommandOption]</c> per catalog
/// entry. Without this cap, the surface can regrow exactly as it did under ADR-009's
/// un-capped "single source of truth" rule (which prevented drift, not growth) — SC-002
/// requires the count itself be enforced. The cap remains an exact named enumeration, not
/// a count: growing from three to four entries is itself the ADR-024 amendment: adding a
/// fifth would fail the build the same way.
///
/// <c>PathSwitchCatalog</c> is internal (visible only to Grimoire.IntegrationTests), so
/// this rule reaches it via reflection rather than a direct reference — deliberately: a
/// reflection-based read cannot be defeated by simply not extending an
/// InternalsVisibleTo list.
/// </summary>
public class DirectorySwitchSurfaceRuleTests
{
    private static readonly string[] ExpectedSwitchNames = ["--data-dir", "--agent-dir", "--wiki-dir", "--memory-dir"];

    [Fact]
    public void PathSwitchCatalog_ContainsExactlyTheFourRootSwitches()
    {
        var actualNames = CatalogSwitchNames();

        Assert.True(
            actualNames.Count == ExpectedSwitchNames.Length &&
            actualNames.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(ExpectedSwitchNames.OrderBy(n => n, StringComparer.Ordinal)),
            "ADR-024 rule M1: PathSwitchCatalog.All must contain exactly the four root " +
            "switches --data-dir, --agent-dir, --wiki-dir, --memory-dir — no other path switch may exist. " +
            $"Found: {string.Join(", ", actualNames)}");
    }

    [Fact]
    public void HubPathSettings_DeclaresExactlyOneCommandOptionPerCatalogEntry()
    {
        var expectedSwitchNames = CatalogSwitchNames()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var declaredProperties = typeof(HubPathSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToArray();

        var actualSwitchNames = new List<string>();
        foreach (var property in declaredProperties)
        {
            var attribute = property.GetCustomAttribute<CommandOptionAttribute>();
            Assert.True(attribute is not null, $"{nameof(HubPathSettings)}.{property.Name} has no [CommandOption] attribute.");
            actualSwitchNames.Add("--" + Assert.Single(attribute!.LongNames));
        }

        Assert.True(
            declaredProperties.Length == 4,
            $"ADR-024 rule M1: HubPathSettings must declare exactly 4 properties (was {declaredProperties.Length}).");
        Assert.Equal(expectedSwitchNames, actualSwitchNames.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    private static List<string> CatalogSwitchNames()
    {
        var catalogType = typeof(HubPathSettings).Assembly.GetType("Grimoire.Hub.Runtime.Paths.PathSwitchCatalog");
        Assert.True(catalogType is not null, "Grimoire.Hub.Runtime.Paths.PathSwitchCatalog not found via reflection.");

        var allMember = catalogType!.GetField("All", BindingFlags.Public | BindingFlags.Static)
            ?? (MemberInfo?)catalogType.GetProperty("All", BindingFlags.Public | BindingFlags.Static);
        Assert.True(allMember is not null, "PathSwitchCatalog.All member not found via reflection.");

        var catalogEntries = (System.Collections.IEnumerable)(allMember switch
        {
            FieldInfo field => field.GetValue(null)!,
            PropertyInfo property => property.GetValue(null)!,
            _ => throw new InvalidOperationException("Unreachable."),
        });
        var names = new List<string>();
        foreach (var entry in catalogEntries)
        {
            var nameProperty = entry.GetType().GetProperty("Name");
            names.Add((string)nameProperty!.GetValue(entry)!);
        }

        return names;
    }
}
