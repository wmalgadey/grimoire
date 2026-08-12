using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// SC-003 (ADR-022, amended by ADR-024): each of the four root options (<c>Data:Dir</c>/
/// <c>Wiki:Dir</c>/<c>Agent:Dir</c>/<c>Memory:Dir</c>) resolves per the documented
/// precedence — command line > environment > appsettings.json (config file) — evaluated
/// independently per option, not as an all-or-nothing group. There is no fourth ("code
/// default") tier: a root absent from every configured tier is a startup failure, covered
/// separately by <see cref="StartupValidationTests"/>/<see cref="PathLoggingContractTests"/>.
/// <see cref="PathLocation.Source"/> reports whichever channel actually won. Environment
/// variable names are the **nested** form (<c>Grimoire__Paths__Memory__Dir</c>, not
/// <c>Grimoire__Paths__MemoryDir</c>) — the flat form is a superseded key that the hub does
/// not detect (spec.md Edge Cases) and silently ignores, falling back to the default.
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class PathPrecedenceTests
{
    private const string DataDirEnvVarName = "Grimoire__Paths__Data__Dir";
    private const string WikiDirEnvVarName = "Grimoire__Paths__Wiki__Dir";

    [Fact]
    public void DataDir_ResolvesPerChannelPrecedence_AndSourceReportsTheWinningChannel()
    {
        RunPrecedenceMatrix(
            "Data:Dir", "data_dir", "--data-dir", DataDirEnvVarName,
            (options, value) => options.Data.Dir = value);
    }

    [Fact]
    public void WikiDir_ResolvesPerChannelPrecedence_AndSourceReportsTheWinningChannel()
    {
        RunPrecedenceMatrix(
            "Wiki:Dir", "wiki_dir", "--wiki-dir", WikiDirEnvVarName,
            (options, value) => options.Wiki.Dir = value);
    }

    /// <summary>
    /// 022-memory-directory-root (SC-003): the memory root resolves per the same
    /// three-tier precedence as the other three roots, independently.
    /// </summary>
    [Fact]
    public void MemoryDir_ResolvesPerChannelPrecedence_AndSourceReportsTheWinningChannel()
    {
        RunPrecedenceMatrix(
            "Memory:Dir", "memory_dir", "--memory-dir", "Grimoire__Paths__Memory__Dir",
            (options, value) => options.Memory.Dir = value);
    }

    /// <summary>
    /// AgentDir is a RequiredInput (not auto-created), so — unlike DataDir/WikiDir — each
    /// candidate location in this matrix must independently satisfy the full agent-runtime
    /// validation before precedence is even observable.
    /// </summary>
    [Fact]
    public void AgentDir_ResolvesPerChannelPrecedence_AndSourceReportsTheWinningChannel()
    {
        const string envVarName = "Grimoire__Paths__Agent__Dir";
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-agentdir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var jsonConfigPath = Path.Combine(root, "appsettings.test.json");
        var cliAgentDir = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-agentdir-cli-{Guid.NewGuid():N}");
        var envAgentDir = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-agentdir-env-{Guid.NewGuid():N}");
        var configAgentDir = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-agentdir-config-{Guid.NewGuid():N}");
        PathConfigurationTestHelpers.SeedAgentRuntimeAt(cliAgentDir);
        PathConfigurationTestHelpers.SeedAgentRuntimeAt(envAgentDir);
        PathConfigurationTestHelpers.SeedAgentRuntimeAt(configAgentDir);

        Environment.SetEnvironmentVariable(envVarName, null);
        try
        {
            File.WriteAllText(jsonConfigPath, BuildNestedPathsJson("Agent:Dir", configAgentDir));

            Environment.SetEnvironmentVariable(envVarName, envAgentDir);
            var winningLocation = ResolveLocation(root, "agent_dir", "Agent:Dir", "--agent-dir", jsonConfigPath, ["--agent-dir", cliAgentDir], (_, _) => { });
            Assert.Equal(Path.GetFullPath(cliAgentDir), winningLocation.ResolvedPath);
            Assert.Equal("command-line", winningLocation.Source);

            winningLocation = ResolveLocation(root, "agent_dir", "Agent:Dir", "--agent-dir", jsonConfigPath, cliArgs: null, (_, _) => { });
            Assert.Equal(Path.GetFullPath(envAgentDir), winningLocation.ResolvedPath);
            Assert.Equal("environment", winningLocation.Source);

            Environment.SetEnvironmentVariable(envVarName, null);
            winningLocation = ResolveLocation(root, "agent_dir", "Agent:Dir", "--agent-dir", jsonConfigPath, cliArgs: null, (_, _) => { });
            Assert.Equal(Path.GetFullPath(configAgentDir), winningLocation.ResolvedPath);
            Assert.Equal("config-file", winningLocation.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
            foreach (var dir in new[] { root, cliAgentDir, envAgentDir, configAgentDir })
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
    }

    /// <summary>
    /// SC-003 mixed case (contracts/directory-options.md §2 worked example): one option
    /// set from the command line, a different option set from the environment, a third
    /// left at the config-file tier — each resolves its own winning channel independently.
    /// </summary>
    [Fact]
    public void MixedChannelsAcrossDifferentOptions_EachResolvesItsOwnWinningChannel_Independently()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-mixed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var cliDataDir = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-mixed-cli-{Guid.NewGuid():N}");
        var envWikiDir = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-mixed-env-{Guid.NewGuid():N}");

        Environment.SetEnvironmentVariable(DataDirEnvVarName, null);
        Environment.SetEnvironmentVariable(WikiDirEnvVarName, null);
        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);

            Environment.SetEnvironmentVariable(WikiDirEnvVarName, envWikiDir);
            var configRoot = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddCommandLine(
                    ["--data-dir", cliDataDir],
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["--data-dir"] = "Grimoire:Paths:Data:Dir" })
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            var dataDirLocation = resolved.Locations.Single(l => l.Name == "data_dir");
            var wikiDirLocation = resolved.Locations.Single(l => l.Name == "wiki_dir");
            var agentDirLocation = resolved.Locations.Single(l => l.Name == "agent_dir");

            Assert.Equal(Path.GetFullPath(cliDataDir), dataDirLocation.ResolvedPath);
            Assert.Equal("command-line", dataDirLocation.Source);

            Assert.Equal(Path.GetFullPath(envWikiDir), wikiDirLocation.ResolvedPath);
            Assert.Equal("environment", wikiDirLocation.Source);

            // AgentDir was seeded explicitly via the helper's options object (not routed
            // through a config provider) — still reports as config-file, the tier every
            // location falls to once no CLI/env override applies (no fourth tier exists).
            Assert.Equal("config-file", agentDirLocation.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirEnvVarName, null);
            Environment.SetEnvironmentVariable(WikiDirEnvVarName, null);
            foreach (var dir in new[] { root, cliDataDir, envWikiDir })
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
    }

    private static void RunPrecedenceMatrix(
        string configKeySuffix, string locationName, string cliSwitch, string envVarName,
        Action<GrimoirePathOptions, string> setDirectValue)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var jsonConfigPath = Path.Combine(root, "appsettings.test.json");
        var cliValue = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-cli-{Guid.NewGuid():N}");
        var envValue = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-env-{Guid.NewGuid():N}");
        var configValue = Path.Combine(Path.GetTempPath(), $"grimoire-precedence-config-{Guid.NewGuid():N}");

        Environment.SetEnvironmentVariable(envVarName, null);
        try
        {
            File.WriteAllText(jsonConfigPath, BuildNestedPathsJson(configKeySuffix, configValue));

            // All three channels available; command line must win.
            Environment.SetEnvironmentVariable(envVarName, envValue);
            var cliArgs = new[] { cliSwitch, cliValue };
            var winningLocation = ResolveLocation(root, locationName, configKeySuffix, cliSwitch, jsonConfigPath, cliArgs, setDirectValue);
            Assert.Equal(Path.GetFullPath(cliValue), winningLocation.ResolvedPath);
            Assert.Equal("command-line", winningLocation.Source);

            // Drop the CLI switch; environment must win over the config file.
            winningLocation = ResolveLocation(root, locationName, configKeySuffix, cliSwitch, jsonConfigPath, cliArgs: null, setDirectValue);
            Assert.Equal(Path.GetFullPath(envValue), winningLocation.ResolvedPath);
            Assert.Equal("environment", winningLocation.Source);

            // Drop the environment variable too; the config file wins — the last tier
            // (ADR-022: no fourth "code default" tier exists any more).
            Environment.SetEnvironmentVariable(envVarName, null);
            winningLocation = ResolveLocation(root, locationName, configKeySuffix, cliSwitch, jsonConfigPath, cliArgs: null, setDirectValue);
            Assert.Equal(Path.GetFullPath(configValue), winningLocation.ResolvedPath);
            Assert.Equal("config-file", winningLocation.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
            foreach (var dir in new[] { root, cliValue, envValue, configValue })
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
    }

    private static PathLocation ResolveLocation(
        string root, string locationName, string configKeySuffix, string cliSwitch, string? jsonConfigPath,
        string[]? cliArgs, Action<GrimoirePathOptions, string> setDirectValue)
    {
        var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);

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
                [cliSwitch] = $"Grimoire:Paths:{configKeySuffix}",
            });
        }
        var configRoot = builder.Build();

        configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);
        var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);
        return resolved.Locations.Single(l => l.Name == locationName);
    }

    /// <summary>
    /// Builds a minimal <c>Grimoire:Paths</c> JSON document with a single nested key set
    /// (e.g. <c>configKeySuffix</c> = <c>"Memory:Dir"</c> → <c>{"Grimoire":{"Paths":{"Memory":{"Dir":"value"}}}}</c>)
    /// — the grouped shape ADR-024 requires (research R8).
    /// </summary>
    private static string BuildNestedPathsJson(string configKeySuffix, string value)
    {
        var json = "\"" + JsonEscape(value) + "\"";
        var parts = configKeySuffix.Split(':');
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            json = "{ \"" + parts[i] + "\": " + json + " }";
        }
        return "{ \"Grimoire\": { \"Paths\": " + json + " } }";
    }

    private static string JsonEscape(string value) => value.Replace("\\", "\\\\");
}
