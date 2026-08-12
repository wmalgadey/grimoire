using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// FR-014/SC-010 (022-memory-directory-root, ADR-024 research R8) — every one of the
/// eleven flat configuration keys superseded by the <c>Grimoire:Paths</c> regrouping is
/// detected and rejected at startup, naming its replacement, rather than being silently
/// ignored and resolved to a default. Table-driven over all eleven keys, exercised through
/// both the configuration-file and the environment-variable tier — the environment tier
/// is the one where the silent fallback would actually bite an operator, since an
/// unrecognized configuration key is normally just ignored (unlike an unrecognized CLI
/// switch, which is already a parser error).
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class SupersededConfigurationKeyTests
{
    public static IEnumerable<object[]> SupersededKeys() =>
    [
        ["Grimoire:Paths:DataDir", "Grimoire__Paths__DataDir", "Grimoire:Paths:Data:Dir"],
        ["Grimoire:Paths:WikiDir", "Grimoire__Paths__WikiDir", "Grimoire:Paths:Wiki:Dir"],
        ["Grimoire:Paths:AgentDir", "Grimoire__Paths__AgentDir", "Grimoire:Paths:Agent:Dir"],
        ["Grimoire:Paths:MemoryDir", "Grimoire__Paths__MemoryDir", "Grimoire:Paths:Memory:Dir"],
        ["Grimoire:Paths:RawDir", "Grimoire__Paths__RawDir", "Grimoire:Paths:Data:RawDir"],
        ["Grimoire:Paths:StateDb", "Grimoire__Paths__StateDb", "Grimoire:Paths:Data:StateDb"],
        ["Grimoire:Paths:WriteLocksDir", "Grimoire__Paths__WriteLocksDir", "Grimoire:Paths:Data:WriteLocksDir"],
        ["Grimoire:Paths:TasksDir", "Grimoire__Paths__TasksDir", "Grimoire:Paths:Memory:TasksDir"],
        ["Grimoire:Paths:ConversationsDir", "Grimoire__Paths__ConversationsDir", "Grimoire:Paths:Memory:ConversationsDir"],
        ["Grimoire:Paths:FindingsDir", "Grimoire__Paths__FindingsDir", "Grimoire:Paths:Memory:FindingsDir"],
        ["Grimoire:Paths:RemediationTasksDir", "Grimoire__Paths__RemediationTasksDir", "Grimoire:Paths:Memory:RemediationTasksDir"],
    ];

    [Theory]
    [MemberData(nameof(SupersededKeys))]
    public void SupersededKeyViaConfigurationFile_FailsAtStartup_NamingItAndItsReplacement(
        string legacyKey, string legacyEnvVar, string replacementKey)
    {
        _ = legacyEnvVar;
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-superseded-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);
            var configRoot = new ConfigurationBuilder()
                .AddInMemoryCollection([new(legacyKey, "some-value")])
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var exception = Assert.Throws<GrimoirePathConfigurationSupersededException>(
                () => GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance));

            Assert.Contains(legacyKey, exception.SupersededKeys);
            Assert.Contains(replacementKey, exception.Replacements);
            Assert.Contains(legacyKey, exception.Message, StringComparison.Ordinal);
            Assert.Contains(replacementKey, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [MemberData(nameof(SupersededKeys))]
    public void SupersededKeyViaEnvironmentVariable_FailsAtStartup_AndDoesNotSilentlyResolveToDefault(
        string legacyKey, string legacyEnvVar, string replacementKey)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-superseded-env-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        Environment.SetEnvironmentVariable(legacyEnvVar, null);
        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);

            Environment.SetEnvironmentVariable(legacyEnvVar, "some-value");
            var configRoot = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var exception = Assert.Throws<GrimoirePathConfigurationSupersededException>(
                () => GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance));

            Assert.Contains(legacyKey, exception.SupersededKeys);
            Assert.Contains(replacementKey, exception.Replacements);
        }
        finally
        {
            Environment.SetEnvironmentVariable(legacyEnvVar, null);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// FR-014 ordering rule: a configuration supplying only the legacy
    /// <c>Grimoire:Paths:MemoryDir</c> key must be reported as superseded, not missing —
    /// "missing" would send the operator looking for a key they already set. Asserted
    /// separately from the table-driven cases above because it is specifically about the
    /// superseded probe running *before* the mandatory-root gate.
    /// </summary>
    [Fact]
    public void ConfigurationSupplyingOnlyTheLegacyMemoryDirKey_IsReportedAsSuperseded_NotMissing()
    {
        var configRoot = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Grimoire:Paths:MemoryDir", "/tmp/some-memory-dir")])
            .Build();
        var options = new GrimoirePathOptions();
        configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

        var exception = Assert.Throws<GrimoirePathConfigurationSupersededException>(
            () => GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance));

        Assert.Contains("Grimoire:Paths:MemoryDir", exception.SupersededKeys);
    }

    /// <summary>
    /// FR-014/SC-010: each superseded-key detection increments the existing failure
    /// counter with the new <c>reason=configuration_superseded</c> label value, and emits
    /// the new <c>paths_configuration_superseded</c> ERROR event.
    /// </summary>
    [Fact]
    public void SupersededKey_Emits_PathsConfigurationSupersededEvent_AtErrorLevel()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-superseded-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);
            var configRoot = new ConfigurationBuilder()
                .AddInMemoryCollection([new("Grimoire:Paths:TasksDir", "some-value")])
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);
            var logger = new CaptureLogger<SupersededConfigurationKeyTests>();

            Assert.Throws<GrimoirePathConfigurationSupersededException>(
                () => GrimoirePathResolver.Resolve(options, configRoot, logger));

            var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "paths_configuration_superseded"));
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Contains("Grimoire:Paths:TasksDir", entry.Fields["superseded_keys"]?.ToString(), StringComparison.Ordinal);
            Assert.Contains("Grimoire:Paths:Memory:TasksDir", entry.Fields["replacements"]?.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
