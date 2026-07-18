using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// T012t (US1, FR-005) — the same logical location supplied through multiple
/// configuration channels resolves per the documented precedence: command line >
/// environment > appsettings.json (config file) > code default. <see cref="PathLocation.Source"/>
/// reports whichever channel actually won.
/// </summary>
public class PathPrecedenceTests
{
    private const string EnvVarName = "Grimoire__Paths__ContentRoot";

    [Fact]
    public void SameLocation_ResolvesPerChannelPrecedence_AndSourceReportsTheWinningChannel()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);
        var jsonConfigPath = Path.Combine(baseDir, "appsettings.test.json");
        var cliWiki = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-cli-{Guid.NewGuid():N}");
        var envWiki = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-env-{Guid.NewGuid():N}");
        var configWiki = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-config-{Guid.NewGuid():N}");

        Environment.SetEnvironmentVariable(EnvVarName, null);
        try
        {
            File.WriteAllText(jsonConfigPath, $$"""
                {
                  "Grimoire": { "Paths": { "ContentRoot": "{{JsonEscape(configWiki)}}" } }
                }
                """);

            // All four channels available; command line must win.
            Environment.SetEnvironmentVariable(EnvVarName, envWiki);
            var cliArgs = new[] { "--content-root", cliWiki };
            var winningLocation = ResolveContentRootLocation(baseDir, jsonConfigPath, cliArgs);
            Assert.Equal(Path.GetFullPath(cliWiki), winningLocation.ResolvedPath);
            Assert.Equal("command-line", winningLocation.Source);

            // Drop the CLI switch; environment must win over the config file.
            winningLocation = ResolveContentRootLocation(baseDir, jsonConfigPath, cliArgs: null);
            Assert.Equal(Path.GetFullPath(envWiki), winningLocation.ResolvedPath);
            Assert.Equal("environment", winningLocation.Source);

            // Drop the environment variable too; the config file must win over the default.
            Environment.SetEnvironmentVariable(EnvVarName, null);
            winningLocation = ResolveContentRootLocation(baseDir, jsonConfigPath, cliArgs: null);
            Assert.Equal(Path.GetFullPath(configWiki), winningLocation.ResolvedPath);
            Assert.Equal("config-file", winningLocation.Source);

            // Drop the config file too; the code default (`<base>/wiki`) applies.
            winningLocation = ResolveContentRootLocation(baseDir, jsonConfigPath: null, cliArgs: null);
            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "wiki")), winningLocation.ResolvedPath);
            Assert.Equal("default", winningLocation.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, null);
            foreach (var dir in new[] { baseDir, cliWiki, envWiki, configWiki })
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
    }

    private static PathLocation ResolveContentRootLocation(string baseDir, string? jsonConfigPath, string[]? cliArgs)
    {
        var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);

        var builder = new ConfigurationBuilder();
        if (jsonConfigPath is not null)
        {
            builder.AddJsonFile(jsonConfigPath, optional: false);
        }
        builder.AddEnvironmentVariables();
        if (cliArgs is not null)
        {
            builder.AddCommandLine(cliArgs, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["--content-root"] = "Grimoire:Paths:ContentRoot",
            });
        }
        var configRoot = builder.Build();

        configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);
        var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);
        return resolved.Locations.Single(l => l.Name == "content_root");
    }

    private static string JsonEscape(string value) => value.Replace("\\", "\\\\");
}
