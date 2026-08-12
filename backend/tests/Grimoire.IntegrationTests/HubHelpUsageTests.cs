using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Grimoire.EvalRunner.Workspace;
using Grimoire.Hub.Cli;
using Grimoire.Hub.Runtime.Paths;
using Spectre.Console.Cli;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T001/T002 (017-hub-help-usage, ADR-009): parity + process-spawn tests for the Hub's
/// `--help`/`-h` usage output. Spawns the actual built <c>Grimoire.Hub</c> executable —
/// the same out-of-process pattern as <see cref="ReplayAdapterTests"/> /
/// <see cref="CrossProcessFileLockTests"/> — because <c>WebApplicationFactory</c> boots
/// the host in-process and could never observe "the process exited before app.Run()"
/// (research.md "How to test process exit / no-server-start behavior").
/// </summary>
public class HubHelpUsageTests
{
    // Root-only marker: unlike a font-specific glyph run, this tagline is intentionally
    // stable across Figlet font changes, while still proving the root-only rendering
    // happened. It is now the DEFAULT COMMAND's description (HubCliApp's WithDescription),
    // so Spectre renders it in its own DESCRIPTION: section rather than HubCliHelpProvider
    // placing it — which is also why the trailing period is absent here: Spectre's
    // HelpProvider trims one from every description it renders (TrimTrailingPeriod).
    private const string RootTagline = "Grimoire is an AI harness that keeps a wiki current through supervised agents";

    [Fact]
    public async Task Help_PrintsUsage_ExitsZeroPromptly_AndNeverStartsTheWebServer()
    {
        var result = await RunHubAsync(["--help"]);

        Assert.False(result.TimedOut, "The --help invocation must exit promptly instead of starting the web host.");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Now listening on:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains(RootTagline, result.StdOut, StringComparison.Ordinal);
        Assert.Contains("How to start the server:", result.StdOut, StringComparison.Ordinal);

        // ADR-024 M1: the fourth root switch is listed alongside the pre-existing three.
        Assert.Contains("--memory-dir", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_CombinedWithBogusDataDir_StillWinsAndExitsZero()
    {
        // FR-004: --help must win before any path resolution is attempted against this
        // (deliberately nonexistent) --data-dir value — proven by the process exiting
        // promptly rather than failing on/creating the bogus path.
        var bogusPath = Path.Combine(Path.GetTempPath(), $"grimoire-help-bogus-{Guid.NewGuid():N}");

        var result = await RunHubAsync(["--help", "--data-dir", bogusPath]);

        Assert.False(result.TimedOut, "--help combined with other args must still exit promptly.");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Now listening on:", result.StdOut, StringComparison.Ordinal);
        Assert.False(Directory.Exists(bogusPath), "No path resolution against the bogus --data-dir may be attempted.");
    }

    /// <summary>
    /// The single permitted Spectre wire-up test for the help path (constitution v1.9.0,
    /// Principle II "Test what we own"): whether a command's own <c>--help</c> lists its
    /// arguments/options in the OPTIONS: grid is Spectre's rendering, not ours, and is left
    /// unverified here — that command-shape coverage lives in-process at
    /// <c>HubCliCommandTests</c>/<c>HubCliQueryCommandTests</c>. What IS ours, and what this
    /// test exists to prove, is <see cref="Grimoire.Hub.Cli.HubCliHelpProvider"/>'s
    /// root-vs-command distinction (research.md D3/D7): per-command help must omit the
    /// root-only tagline and server-start footer, and must exit 0 without ever reaching
    /// ExecuteAsync. Sampled across 3 of the 8 commands for variety, not to prove their
    /// individual option shapes.
    /// </summary>
    [Theory]
    [InlineData("lint-run")]
    [InlineData("query")]
    [InlineData("remediation-authorize")]
    public async Task PerCommandHelp_OmitsRootOnlyLogoAndGuidance_AndNeverExecutes(string commandName)
    {
        var result = await RunHubAsync([commandName, "--help"]);

        Assert.False(result.TimedOut, $"{commandName} --help must exit promptly.");
        Assert.Equal(0, result.ExitCode);

        // The tagline is the DEFAULT command's description, so a named command's help must
        // not carry it — this is what proves HubCliHelpProvider's IsDefaultCommand check
        // still distinguishes root from per-command rendering.
        Assert.DoesNotContain(RootTagline, result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("How to start the server:", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// T036 (018-hub-cli-commands, contracts/cli-commands.md "Global rules": "Unknown
    /// first-argument command name → usage error naming the unknown command, exit 2").
    /// Re-verified after Phase 6's <c>--query</c>/dispatch changes: <c>ShouldDispatchToCli</c>
    /// (Program.cs) still routes any bareword first argument to the CommandApp. The message
    /// text itself is Spectre's own unknown-command validation, not ours, and is left
    /// unverified here — what this test proves is ours: <c>HubCliApp</c>'s
    /// <c>PropagateExceptions()</c> handling maps that failure to exit 2, and the server is
    /// never started while doing so.
    /// </summary>
    [Fact]
    public async Task UnknownCommandName_PrintsUsageError_ExitsTwo()
    {
        var result = await RunHubAsync(["no-such-command"]);

        Assert.False(result.TimedOut, "An unknown command name must fail fast, not hang.");
        Assert.Equal(2, result.ExitCode);
        Assert.DoesNotContain("Now listening on:", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// T036 (018-hub-cli-commands, SC-001 "never binds a port"): the safe out-of-process
    /// case for a new BLOCKING command (<c>remediation-authorize</c> would otherwise
    /// require a real spawned agent process to reach a terminal state, which an
    /// out-of-process spawn test cannot script — <see cref="FakeAgentProcessLauncher"/>
    /// only exists in-process). A missing required <c>--task-id</c> fails Spectre's own
    /// settings validation before <see cref="Grimoire.Hub.Cli.HubCliTypeRegistrar"/> ever
    /// builds the Hub composition (research.md D8/ADR-020) — so this exercises the exact
    /// same "no host built, no port bound, no path resolution attempted" guarantee
    /// <see cref="Help_CombinedWithBogusBaseDir_StillWinsAndExitsZero"/> proves for
    /// <c>--help</c>, but for a real command invocation instead. The exit-2-on-a-missing-
    /// required-option failure itself is Spectre's own enforcement (its mapping to exit 2
    /// is already proven by <see cref="UnknownCommandName_PrintsUsageError_ExitsTwo"/>) and
    /// is left unverified here — what this test proves is ours: no path resolution against
    /// the bogus <c>--data-dir</c> is ever attempted.
    /// </summary>
    [Fact]
    public async Task RemediationAuthorize_MissingRequiredTaskId_NeverResolvesPaths()
    {
        var bogusPath = Path.Combine(Path.GetTempPath(), $"grimoire-help-bogus-{Guid.NewGuid():N}");

        var result = await RunHubAsync(["remediation-authorize", "--data-dir", bogusPath]);

        Assert.False(result.TimedOut, "A missing required option must fail validation promptly, not hang.");
        Assert.False(
            Directory.Exists(bogusPath),
            "Settings validation must fail before any path resolution against --data-dir is attempted.");
    }

    /// <summary>
    /// T036 (018-hub-cli-commands, quickstart.md validation, T040): regression guard for a
    /// severe bug quickstart.md's real out-of-process walkthrough surfaced (invisible to
    /// every other test in this feature, all 659+ of which construct command classes
    /// directly — <c>new RemediationDismissCommand(service, stdout)</c> — bypassing
    /// Spectre's real dispatch entirely). Every multi-constructor command (all 7 new ones
    /// — every command except the pre-existing <c>submit-source</c>, which has only one
    /// constructor) failed with <c>"Could not resolve type '…'."</c> on every genuine
    /// invocation: <see cref="Grimoire.Hub.Cli.HubCliTypeRegistrar"/>'s composite resolver
    /// answers <c>Microsoft.Extensions.DependencyInjection.ActivatorUtilities</c>'
    /// <c>IServiceProviderIsService</c> probe with its own small supplementary container's
    /// narrow view (which reflects only Spectre's internal registrations, never the real
    /// host's services) instead of a view that accounts for the host fallback — making
    /// every real command dependency look unresolvable during constructor selection, even
    /// though actual VALUE resolution would have succeeded. Fixed by having
    /// <c>HubCliTypeResolver</c> answer that probe with its own permissive
    /// <c>IServiceProviderIsService</c> implementation instead of shadowing on the
    /// supplementary container's built-in one (see the fix's own doc comment for the full
    /// mechanism). This test proves a real command reaches its ExecuteAsync body — not
    /// merely a validation or help path — via the actual built binary, spawned exactly
    /// like a real operator/script invocation, with no test-only construction shortcut.
    /// <c>remediation-dismiss</c> against an empty (never-seeded) scratch data directory
    /// is the fastest such invocation: no agent work, deterministic not-found outcome.
    /// </summary>
    [Fact]
    public async Task RemediationDismiss_RealOutOfProcessInvocation_ReachesExecuteAsync_ViaActivatorUtilities()
    {
        var repoRoot = EvalPaths.Discover().RepoRoot;
        var scratchDir = CreateScratchDataDirectory();

        try
        {
            // ADR-022: only --data-dir/--wiki-dir/--agent-dir exist; per-agent worker
            // switches are gone (FR-008 — a single --agent-dir governs the whole agent
            // runtime). --agent-dir points at the repo's own solution-wide build output
            // (.grimoire/agents), populated by the normal `dotnet build`/`dotnet test`
            // ProjectReference chain (Grimoire.IntegrationTests references all three agent
            // projects) — never copied or reconstructed by this test.
            var agentDir = Path.Combine(repoRoot, ".grimoire", "agents");
            var result = await RunHubAsync(
                [
                    "remediation-dismiss",
                    "--data-dir", Path.Combine(scratchDir, "data"),
                    "--wiki-dir", Path.Combine(scratchDir, "wiki"),
                    "--agent-dir", agentDir,
                    "--task-id", "does-not-exist",
                ],
                workingDirectory: scratchDir);

            Assert.False(result.TimedOut, "A real remediation-dismiss invocation must exit promptly, not hang.");
            Assert.Equal((int)CliExitCode.NotFound, result.ExitCode);
            Assert.Equal("Remediation task 'does-not-exist' was not found.", result.StdOut.Trim());
        }
        finally
        {
            Directory.Delete(scratchDir, recursive: true);
        }
    }

    /// <summary>
    /// ADR-022 quickstart validation finding (Scenario 3/6): a path-resolution failure
    /// hit while Spectre lazily resolves a real command's constructor dependencies (as
    /// opposed to the always-eager <c>HubHostComposition</c> path the web-host/server-mode
    /// invocation takes) arrives at <c>HubCliApp.RunAsync</c> wrapped in Spectre's own
    /// generic <c>CommandRuntimeException</c> ("Could not resolve type '...'.") unless
    /// unwrapped — this only reproduces through the real out-of-process CLI dispatch path
    /// (an in-process command construction, as most tests in this file use, bypasses
    /// Spectre's type resolver entirely and never hits the wrapping).
    /// </summary>
    [Fact]
    public async Task RemediationDismiss_RealOutOfProcessInvocation_MissingAgentDir_ReportsTheRealMessage_NotSpectresGenericResolutionFailure()
    {
        var scratchDir = CreateScratchDataDirectory();
        var missingAgentDir = Path.Combine(scratchDir, "no-such-agent-dir");

        try
        {
            var result = await RunHubAsync(
                [
                    "remediation-dismiss",
                    "--data-dir", Path.Combine(scratchDir, "data"),
                    "--wiki-dir", Path.Combine(scratchDir, "wiki"),
                    "--agent-dir", missingAgentDir,
                    "--task-id", "does-not-exist",
                ],
                workingDirectory: scratchDir);

            Assert.False(result.TimedOut, "A real remediation-dismiss invocation must exit promptly, not hang.");
            Assert.Equal((int)CliExitCode.OperationFailed, result.ExitCode);
            Assert.DoesNotContain("Could not resolve type", result.StdErr, StringComparison.Ordinal);
            Assert.Contains("agent_dir", result.StdErr, StringComparison.Ordinal);
            Assert.Contains(Path.GetFullPath(missingAgentDir), result.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(scratchDir, recursive: true);
        }
    }

    /// <summary>
    /// 022-memory-directory-root FR-014/SC-010, real out-of-process regression coverage:
    /// a superseded flat configuration key supplied through the environment must reach
    /// the operator as <see cref="Grimoire.Hub.Runtime.Paths.GrimoirePathConfigurationSupersededException"/>'s
    /// own actionable message — naming the key and its replacement — not Spectre's
    /// generic "Could not resolve type" wrapping. Regression: <c>HubCliApp</c>'s
    /// <c>UnwrapPathResolutionFailure</c> originally unwrapped only
    /// <c>GrimoirePathValidationException</c>/<c>GrimoirePathConfigurationMissingException</c>,
    /// silently swallowing the newer superseded-key exception behind the CLI's lazy
    /// Spectre type-resolution path (found by manual quickstart validation, not by any
    /// in-process test, since in-process command construction bypasses Spectre's
    /// resolver entirely).
    /// </summary>
    [Fact]
    public async Task RemediationDismiss_RealOutOfProcessInvocation_SupersededMemoryDirKey_ReportsTheRealMessage_NotSpectresGenericResolutionFailure()
    {
        var scratchDir = CreateScratchDataDirectory();
        var repoRoot = EvalPaths.Discover().RepoRoot;
        var agentDir = Path.Combine(repoRoot, ".grimoire", "agents");
        const string legacyEnvVar = "Grimoire__Paths__MemoryDir";

        try
        {
            // Set only on the spawned child's environment (via ProcessStartInfo below),
            // never on this test process's own — Environment.SetEnvironmentVariable here
            // would be process-wide and race with any other test reading Grimoire__Paths__*
            // concurrently (xUnit's default cross-class parallelism), exactly the failure
            // mode PathPrecedenceTests/SupersededConfigurationKeyTests guard against for
            // their own env var mutations.
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = scratchDir,
            };
            startInfo.ArgumentList.Add(ResolveHubDllPath(repoRoot));
            foreach (var arg in new[]
            {
                "remediation-dismiss",
                "--data-dir", Path.Combine(scratchDir, "data"),
                "--wiki-dir", Path.Combine(scratchDir, "wiki"),
                "--agent-dir", agentDir,
                "--task-id", "does-not-exist",
            })
            {
                startInfo.ArgumentList.Add(arg);
            }
            startInfo.EnvironmentVariables[legacyEnvVar] = "/does/not/matter";

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start the Grimoire.Hub process.");
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cts.Token);
            var stdErr = await stdErrTask;
            _ = await stdOutTask;

            Assert.Equal((int)CliExitCode.OperationFailed, process.ExitCode);
            Assert.DoesNotContain("Could not resolve type", stdErr, StringComparison.Ordinal);
            Assert.Contains("Grimoire:Paths:MemoryDir", stdErr, StringComparison.Ordinal);
            Assert.Contains("Grimoire:Paths:Memory:Dir", stdErr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(scratchDir, recursive: true);
        }
    }

    /// <summary>
    /// A scratch directory containing only the one thing ADR-022 anchors at the process
    /// working directory rather than any of the three roots: the secrets file
    /// (FR-019 — <c>GrimoirePathResolver.Resolve</c> only checks it EXISTS, never reads
    /// its contents unless a command actually spawns an agent, which
    /// <c>remediation-dismiss</c> never does). <c>--data-dir</c>/<c>--wiki-dir</c> point
    /// at sibling subfolders the resolver auto-creates; <c>--agent-dir</c> points at the
    /// repo's real build output instead of anything under this scratch directory.
    /// </summary>
    private static string CreateScratchDataDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-hub-cli-realdispatch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".env"), string.Empty);
        return root;
    }

    /// <summary>
    /// T013 (018-hub-cli-commands, research.md D4): an in-process parity check between
    /// <see cref="HubPathSettings"/>'s declared <c>[CommandOption]</c> properties and
    /// <see cref="PathSwitchCatalog.All"/> — the two sources D4 requires to stay a strict
    /// 1:1 mapping (every path switch gets exactly one Spectre option, and Spectre never
    /// grows an option the catalog — and therefore the web host's own switch handling —
    /// doesn't know about). Unlike the other tests in this file, this does not spawn the
    /// Hub process: it reflects directly over the settings type.
    /// </summary>
    [Fact]
    public void HubPathSettings_DeclaresExactlyOneCommandOptionPerPathSwitchCatalogEntry()
    {
        var expectedSwitchNames = PathSwitchCatalog.All
            .Select(s => s.Name)
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

        // Every declared property must carry exactly one [CommandOption]; the loop above
        // already asserts that, so this only needs to confirm the count lines up with the
        // catalog (catches a stray non-path property slipping in).
        Assert.Equal(declaredProperties.Length, actualSwitchNames.Count);
        Assert.Equal(expectedSwitchNames, actualSwitchNames.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The help text an operator reads comes from <see cref="HubPathSettings"/>'s
    /// <c>[Description]</c> attributes — Spectre renders the settings type, not the catalog.
    /// <see cref="PathSwitchCatalog"/> carries the same descriptions and is the documented
    /// single declaration point (ADR-020), so without this assertion its copy could silently
    /// drift from (or outlive) the one actually shown. Extends the 1:1 name parity above to
    /// the text.
    /// </summary>
    [Fact]
    public void HubPathSettings_DescriptionsMatchThePathSwitchCatalogEntryTheyMirror()
    {
        var descriptionsBySwitchName = typeof(HubPathSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(
                property => "--" + property.GetCustomAttribute<CommandOptionAttribute>()!.LongNames.Single(),
                property => property.GetCustomAttribute<DescriptionAttribute>()?.Description,
                StringComparer.Ordinal);

        foreach (var pathSwitch in PathSwitchCatalog.All)
        {
            Assert.True(
                descriptionsBySwitchName.TryGetValue(pathSwitch.Name, out var declaredDescription),
                $"{nameof(HubPathSettings)} declares no [CommandOption] for {pathSwitch.Name}.");
            Assert.Equal(pathSwitch.Description, declaredDescription);
        }
    }

    private static async Task<HubRunResult> RunHubAsync(IReadOnlyList<string> args, string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }
        startInfo.ArgumentList.Add(ResolveHubDllPath(EvalPaths.Discover().RepoRoot));
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Grimoire.Hub process.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already exited between timeout and kill.
            }
        }

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        return new HubRunResult(timedOut ? -1 : process.ExitCode, stdOut, stdErr, timedOut);
    }

    /// <summary>
    /// Mirrors <see cref="Grimoire.EvalRunner.Workspace.AgentProcessInvoker.ResolveAgentDllPath"/>:
    /// the Hub must be launched from its OWN build output (where its deps.json/
    /// runtimeconfig.json resolve every ASP.NET Core dependency correctly), not a copy
    /// inside this test project's output directory.
    /// </summary>
    private static string ResolveHubDllPath(string repoRoot)
    {
        var separator = Path.DirectorySeparatorChar;
        var preferred = AppContext.BaseDirectory.Contains($"{separator}Release{separator}", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Release", "Debug" }
            : ["Debug", "Release"];

        foreach (var configuration in preferred)
        {
            var candidate = Path.Combine(
                repoRoot, "backend", "src", "Grimoire.Hub", "bin", configuration, "net10.0", "Grimoire.Hub.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Grimoire.Hub.dll not found in its build output. Build first: dotnet build backend/Grimoire.slnx");
    }

    private readonly record struct HubRunResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);
}
