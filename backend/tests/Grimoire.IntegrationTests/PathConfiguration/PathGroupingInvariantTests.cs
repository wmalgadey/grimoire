using System.Reflection;
using System.Text;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// ADR-024 rule M5 ("the grouping is the anchoring") — 022-memory-directory-root
/// FR-003/FR-004, SC-002/SC-009: for each of the four anchor groups (Data/Wiki/Agent/
/// Memory) on <see cref="GrimoirePathOptions"/>, relocating that group's <c>Dir</c> alone
/// moves every resolved location derived from a sub-path property of that group, and
/// moves no location derived from any other group. Driven by reflection over the options
/// graph so a sub-path added to a group later is covered automatically without editing
/// this test. This subsumes the 4x4 root-independence matrix SC-002 would otherwise need
/// as a standalone test — relocating each group's <c>Dir</c> alone and checking what moved
/// is precisely the independence assertion, generalized.
/// </summary>
public class PathGroupingInvariantTests
{
    private static readonly string[] GroupNames = ["Data", "Wiki", "Agent", "Memory"];

    public static IEnumerable<object[]> Groups() => GroupNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Groups))]
    public void RelocatingOneGroupsDir_MovesOnlyLocationsDerivedFromThatGroup(string groupName)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-grouping-invariant-{groupName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var relocatedDir = Path.Combine(Path.GetTempPath(), $"grimoire-grouping-invariant-{groupName}-relocated-{Guid.NewGuid():N}");

        try
        {
            var baselineOptions = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(root).Options;
            var baselineConfigRoot = new ConfigurationBuilder().Build();
            var baseline = GrimoirePathResolver.Resolve(baselineOptions, baselineConfigRoot, NullLogger.Instance);

            // Fresh options over the same seeded root — every unrelocated group's Dir
            // resolves to the exact same absolute path as the baseline run, so any
            // divergence there can only be caused by the relocation below.
            var options = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(root).Options;
            var groupProperty = typeof(GrimoirePathOptions).GetProperty(groupName, BindingFlags.Public | BindingFlags.Instance)!;
            var group = groupProperty.GetValue(options)!;
            var dirProperty = group.GetType().GetProperty("Dir", BindingFlags.Public | BindingFlags.Instance)!;

            if (groupName == "Agent")
            {
                // AgentDir is a RequiredInput — the relocated destination must independently
                // satisfy the full agent-runtime validation before resolution can succeed.
                PathConfigurationTestHelpers.SeedAgentRuntimeAt(relocatedDir);
            }
            else
            {
                Directory.CreateDirectory(relocatedDir);
            }
            dirProperty.SetValue(group, relocatedDir);

            var configRoot = new ConfigurationBuilder().Build();
            var relocated = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            var derivedLocationNames = DerivedLocationNames(groupName);
            Assert.NotEmpty(derivedLocationNames);

            foreach (var location in relocated.Locations)
            {
                if (derivedLocationNames.Contains(location.Name))
                {
                    Assert.StartsWith(Path.GetFullPath(relocatedDir), location.ResolvedPath, StringComparison.Ordinal);
                }
                else
                {
                    var baselineLocation = baseline.Locations.Single(l => l.Name == location.Name);
                    Assert.Equal(baselineLocation.ResolvedPath, location.ResolvedPath);
                }
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            if (Directory.Exists(relocatedDir))
            {
                Directory.Delete(relocatedDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Derives the set of <see cref="PathLocation.Name"/> values anchored at
    /// <paramref name="groupName"/>'s <c>Dir</c>, purely from the options graph's shape
    /// (ADR-024 rule M4) — the group's own <c>Dir</c> property maps to
    /// <c>{group}_dir</c>, and every other property on the group type maps to its
    /// PascalCase-to-snake_case name. This mirrors the naming convention
    /// <c>GrimoirePathResolver.BuildLocation</c> already uses, so a sub-path added to a
    /// group later is picked up automatically without editing this test.
    /// </summary>
    private static HashSet<string> DerivedLocationNames(string groupName)
    {
        var groupProperty = typeof(GrimoirePathOptions).GetProperty(groupName, BindingFlags.Public | BindingFlags.Instance)!;
        var subProperties = groupProperty.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        var names = new HashSet<string>();
        foreach (var property in subProperties)
        {
            names.Add(property.Name == "Dir"
                ? $"{ToSnakeCase(groupName)}_dir"
                : ToSnakeCase(property.Name));
        }
        return names;
    }

    private static string ToSnakeCase(string pascalCase)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < pascalCase.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascalCase[i]))
            {
                builder.Append('_');
            }
            builder.Append(char.ToLowerInvariant(pascalCase[i]));
        }
        return builder.ToString();
    }
}
