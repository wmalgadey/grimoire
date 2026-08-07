using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// SC-004/US2 AS1 end-to-end: with only <c>--wiki-dir</c> supplied (simulated here via the
/// same <c>Grimoire:Paths:WikiDir</c> configuration key <see cref="PathSwitchCatalog"/>
/// binds that switch to — AddCommandLine and AddInMemoryCollection populate
/// <see cref="IConfiguration"/> identically), a real agent-produced artifact (a Findings
/// Report, via <see cref="LintRunCoordinator"/>/<see cref="FindingsReportStore"/>) is
/// written to disk under the custom wiki directory, while <c>DataDir</c>/<c>AgentDir</c>
/// stay at their unset, cwd-anchored defaults.
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class WikiDirEndToEndContentTests
{
    [Fact]
    public async Task OnlyWikiDirFlagSet_LintRunFindingsReport_LandsUnderCustomWikiDir_DataAndAgentDirsStayDefault()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-wiki-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();
        var customWikiDir = Path.Combine(Path.GetTempPath(), $"grimoire-wiki-e2e-custom-{Guid.NewGuid():N}");

        try
        {
            Directory.SetCurrentDirectory(cwd);
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);

            // Mirrors exactly what a real `--wiki-dir <path>` invocation binds through
            // AddCommandLine + PathSwitchCatalog: only Grimoire:Paths:WikiDir is set, no
            // other switch.
            var configRoot = new ConfigurationBuilder()
                .AddInMemoryCollection([new("Grimoire:Paths:WikiDir", customWikiDir)])
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(customWikiDir), resolved.WikiDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire")), resolved.DataDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "agents")), resolved.AgentDir);

            var repository = new OperationalStateRepository(resolved.StateDbPath);
            await repository.InitializeAsync();
            var reportStore = new FindingsReportStore(resolved, NullLogger<FindingsReportStore>.Instance);
            var coordinator = new LintRunCoordinator(
                new FakeAgentProcessLauncher(autoPlay: true), reportStore, resolved,
                logger: NullLogger<LintRunCoordinator>.Instance, stateRepository: repository);

            var result = await coordinator.TriggerAsync();
            Assert.IsType<LintSubmissionResult.Accepted>(result);

            await PollAsync.WaitAsync(
                () => !coordinator.IsRunActive,
                TimeSpan.FromSeconds(10),
                "Expected the scripted lint run to reach a terminal state within 10s.");

            var runId = coordinator.LatestRunId;
            Assert.False(string.IsNullOrWhiteSpace(runId));
            var reportPath = resolved.FindingsReportPathFor(runId!);

            Assert.True(File.Exists(reportPath), $"Expected a findings report at {reportPath}.");
            Assert.StartsWith(resolved.WikiDir, reportPath, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.DataDir, reportPath, StringComparison.Ordinal);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            if (Directory.Exists(cwd))
            {
                Directory.Delete(cwd, recursive: true);
            }
            if (Directory.Exists(customWikiDir))
            {
                Directory.Delete(customWikiDir, recursive: true);
            }
        }
    }
}
