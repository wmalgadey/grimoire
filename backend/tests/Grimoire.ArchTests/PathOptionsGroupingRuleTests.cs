using System.Reflection;
using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule M4 for ADR-024: <see cref="GrimoirePathOptions"/> declares
/// exactly four root-group properties (<c>Data</c>, <c>Wiki</c>, <c>Agent</c>,
/// <c>Memory</c>), each of a group type declaring a <c>Dir</c> string property plus zero
/// or more sub-path string properties, and exactly one ungrouped property
/// (<c>SecretsFile</c>). No path-valued (<c>string</c>) property may sit directly on
/// <see cref="GrimoirePathOptions"/> besides <c>SecretsFile</c>.
///
/// This keeps the options graph and the <c>appsettings.json</c> tree the same shape, so
/// the configuration file cannot silently drift back toward flatness one loose property
/// at a time — the grouping the file expresses (research R8) is only worth doing if it
/// stays true.
/// </summary>
public class PathOptionsGroupingRuleTests
{
    private static readonly string[] ExpectedGroupNames = ["Data", "Wiki", "Agent", "Memory"];
    private const string ExpectedUngroupedProperty = "SecretsFile";

    [Fact]
    public void GrimoirePathOptions_DeclaresExactlyFourGroupsPlusOneUngroupedProperty()
    {
        var properties = typeof(GrimoirePathOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        var stringProperties = properties.Where(p => p.PropertyType == typeof(string)).ToList();
        var groupProperties = properties.Where(p => p.PropertyType != typeof(string)).ToList();

        Assert.True(
            stringProperties.Count == 1 && stringProperties[0].Name == ExpectedUngroupedProperty,
            "ADR-024 rule M4: GrimoirePathOptions must declare exactly one ungrouped string " +
            $"property, \"{ExpectedUngroupedProperty}\" — no other path-valued property may sit " +
            $"directly on it. Found ungrouped string properties: {string.Join(", ", stringProperties.Select(p => p.Name))}");

        var groupNames = groupProperties.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var expected = ExpectedGroupNames.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.True(
            groupNames.SequenceEqual(expected),
            "ADR-024 rule M4: GrimoirePathOptions must declare exactly the four root-group " +
            $"properties Data, Wiki, Agent, Memory. Found: {string.Join(", ", groupNames)}");

        foreach (var group in groupProperties)
        {
            var dirProperty = group.PropertyType.GetProperty("Dir", BindingFlags.Public | BindingFlags.Instance);
            Assert.True(
                dirProperty is not null && dirProperty.PropertyType == typeof(string),
                $"ADR-024 rule M4: group type {group.PropertyType.Name} (property " +
                $"GrimoirePathOptions.{group.Name}) must declare a string \"Dir\" property — the root itself.");

            var otherMembers = group.PropertyType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.Name != "Dir");
            Assert.True(
                otherMembers.All(p => p.PropertyType == typeof(string)),
                $"ADR-024 rule M4: every sub-path property on {group.PropertyType.Name} must be a string.");
        }
    }
}
