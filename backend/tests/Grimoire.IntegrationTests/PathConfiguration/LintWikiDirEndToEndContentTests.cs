using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// SC-001/SC-002 end-to-end (022-memory-directory-root): with only <c>--wiki-dir</c>
/// supplied (simulated here via the same <c>Grimoire:Paths:Wiki:Dir</c> configuration key
/// <see cref="PathSwitchCatalog"/> binds that switch to — AddCommandLine and
/// AddInMemoryCollection populate <see cref="IConfiguration"/> identically), a real
/// agent-produced artifact (a Findings Report, via
/// <see cref="LintRunCoordinator"/>/<see cref="FindingsReportStore"/>) is written to disk
/// under the default memory directory — NOT the custom wiki directory, since bookkeeping
/// anchors at <c>MemoryDir</c> — while <c>DataDir</c>/<c>AgentDir</c>/<c>MemoryDir</c> stay
/// at their unset, cwd-anchored defaults.
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class LintWikiDirEndToEndContentTests
{
    [Fact]
    public async Task OnlyWikiDirFlagSet_LintRunFindingsReport_LandsUnderDefaultMemoryDir_NotTheCustomWikiDir()
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
            // AddCommandLine + PathSwitchCatalog: only Grimoire:Paths:Wiki:Dir is set, no
            // other switch.
            var configRoot = new ConfigurationBuilder()
                .AddInMemoryCollection([new("Grimoire:Paths:Wiki:Dir", customWikiDir)])
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(customWikiDir), resolved.WikiDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire")), resolved.DataDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "agents")), resolved.AgentDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "memory")), resolved.MemoryDir);

            var repository = new OperationalStateRepository(resolved.StateDbPath);
            await repository.InitializeAsync();
            var reportStore = new FindingsReportStore(resolved, NullLogger<FindingsReportStore>.Instance);
            var coordinator = new LintRunCoordinator(
                new FakeAgentProcessLauncher(autoPlay: true), reportStore, resolved,
                logger: NullLogger<LintRunCoordinator>.Instance, stateRepository: repository);

            var result = await coordinator.TriggerAsync();
            var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
            var runId = accepted.Run.RunId;

            await PollAsync.WaitAsync(
                () => !coordinator.IsRunActive,
                TimeSpan.FromSeconds(10),
                "Expected the scripted lint run to reach a terminal state within 10s.");

            // LintRunCoordinator.FinishRunAsync flips the run's terminal status (and
            // therefore IsRunActive) slightly before it finishes writing the Findings
            // Report — poll for the file too (mirrors LintRunLifecycleTests.
            // LintCoordinatorHarness.WaitForTerminalAsync's established idiom for this
            // exact race).
            var reportPath = resolved.FindingsReportPathFor(runId);
            await PollAsync.WaitAsync(
                () => File.Exists(reportPath),
                TimeSpan.FromSeconds(5),
                $"Expected a findings report at {reportPath}.");

            Assert.StartsWith(resolved.MemoryDir, reportPath, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.WikiDir, reportPath, StringComparison.Ordinal);
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
