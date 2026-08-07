using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// SC-003 (US3 AS1/AS2) — relocating only <c>DataDir</c> carries every location anchored
/// on it (raw intake, state DB, write-locks, lint pid) to the new root, while
/// <c>SecretsFile</c> — anchored at the process working directory, never at any root
/// (FR-019) — is unaffected, and both the wiki directory and the agent directory stay at
/// their own cwd-anchored defaults rather than nesting inside the relocated data
/// directory (PR #55 reviewer confirmation: AgentDir no longer moves with DataDir).
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class DataDirRelocationTests
{
    [Fact]
    public void CustomDataDir_RelocatesEveryDataDirDerivedLocation_ButLeavesSecretsFileAndAgentDirAtCwd()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-datadir-relocation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();
        var customDataDir = Path.Combine(Path.GetTempPath(), $"grimoire-custom-datadir-{Guid.NewGuid():N}");

        try
        {
            Directory.SetCurrentDirectory(cwd);
            // getcwd() resolves symlinks (macOS temp dirs) — build expectations from the
            // same canonical form the resolver itself observes.
            cwd = Directory.GetCurrentDirectory();

            // Seeds a complete agent runtime at the cwd-anchored default (.grimoire/agents)
            // — AgentDir is left unset here (its own default, unaffected by DataDir).
            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);
            options.DataDir = customDataDir;

            var configRoot = new ConfigurationBuilder().Build();
            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            var expectedDataDir = Path.GetFullPath(customDataDir);
            Assert.Equal(expectedDataDir, resolved.DataDir);

            foreach (var dataDirDerived in new[]
            {
                resolved.RawOriginalsDir,
                resolved.RawSourcesDir,
                resolved.StateDbPath,
                resolved.WriteLocksDir,
                resolved.LintPidPath,
            })
            {
                Assert.StartsWith(expectedDataDir, dataDirDerived, StringComparison.Ordinal);
            }

            // SecretsFile stays anchored at cwd, entirely unaffected by DataDir relocation.
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".env")), resolved.SecretsFilePath);

            // The wiki resolves to its own cwd-anchored default — a sibling of the
            // *default* data directory, not nested inside the relocated one (US3 AS2).
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "llm-wiki")), resolved.WikiDir);
            Assert.DoesNotContain(expectedDataDir, resolved.WikiDir, StringComparison.Ordinal);

            // AgentDir resolves to its own cwd-anchored default too — relocating DataDir
            // does not drag the agent runtime along with it.
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "agents")), resolved.AgentDir);
            Assert.DoesNotContain(expectedDataDir, resolved.AgentDir, StringComparison.Ordinal);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            if (Directory.Exists(cwd))
            {
                Directory.Delete(cwd, recursive: true);
            }
            if (Directory.Exists(customDataDir))
            {
                Directory.Delete(customDataDir, recursive: true);
            }
        }
    }
}
