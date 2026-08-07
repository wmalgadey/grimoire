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
    // Single source of truth per plan.md/spec.md FR-002 for THIS TEST: the 16 ADR-009
    // path switches, sourced directly from PathSwitchCatalog.All (internal, exposed to
    // this project via AssemblyInfo.cs's InternalsVisibleTo) so this list can never
    // independently drift from what Program.cs actually accepts — plus submit-source's
    // own options, which have no equivalent catalog to source from. Deliberately NOT
    // extended to all 8 command names (018-hub-cli-commands T036): this list is also
    // reused by <see cref="SubmitSource_WithHelp_ShowsUsageInsteadOfSubmitting"/> to check
    // a single COMMAND's own --help output, which lists only its own name (via its USAGE
    // line) and its own options — never the other 7 commands. Root help's full 8-command
    // coverage (SC-004) is asserted separately by
    // <see cref="Help_CommandsSection_ListsEveryCommandsOwnDescription"/> below, which
    // sources its expectations from <see cref="HubCliCommands.All"/> directly.
    private static readonly string[] ExpectedSwitches =
    [
        .. PathSwitchCatalog.All.Select(s => s.Name),
        "submit-source",
        "--path",
        "--source-kind",
    ];

    // 018-hub-cli-commands T036 (SC-004): a 30-char prefix of each command's own
    // description, sourced from the same HubCliCommands.All catalog. Root help's
    // Commands: table word-wraps long descriptions at the console width Spectre falls
    // back to for redirected/non-interactive stdout (verified against this file's actual
    // captured output — every description below wraps, if at all, well after 30 chars),
    // so asserting a short prefix instead of the full description avoids false negatives
    // from a wrap point landing mid-string while still proving each command's specific
    // description text (not just its name) is present.
    private static readonly (string Name, string DescriptionPrefix)[] ExpectedCommandDescriptions =
    [
        .. HubCliCommands.All.Select(c => (c.Name, DescriptionPrefix: c.Description[..Math.Min(30, c.Description.Length)])),
    ];

    // 018-hub-cli-commands T036: HubCliHelpProvider (research.md D2/D3/D7) adds the
    // FigletText logo and the "Server options:" section only for the ROOT invocation
    // (command is null) — both in the same early-return branch, so either string is an
    // equally valid "did the logo/root-only sections leak into per-command help" probe.
    // "____" is a run that only the Figlet "Grimoire" rendering produces (checked against
    // this file's captured root-help output); no path switch, command name, or
    // description contains four consecutive underscores.
    private const string FigletLogoMarker = "____";

    [Fact]
    public async Task Help_PrintsUsage_ExitsZeroPromptly_AndNeverStartsTheWebServer()
    {
        var result = await RunHubAsync(["--help"]);

        Assert.False(result.TimedOut, "The --help invocation must exit promptly instead of starting the web host.");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Now listening on:", result.StdOut, StringComparison.Ordinal);
        AssertUsageListsAllSwitches(result.StdOut);
        Assert.Contains(FigletLogoMarker, result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// T036 (018-hub-cli-commands, SC-004): root help's Commands: section names every one
    /// of the 8 commands together with its own description (not merely its name, already
    /// covered by <see cref="Help_PrintsUsage_ExitsZeroPromptly_AndNeverStartsTheWebServer"/>
    /// via <see cref="AssertUsageListsAllSwitches"/>).
    /// </summary>
    [Fact]
    public async Task Help_CommandsSection_ListsEveryCommandsOwnDescription()
    {
        var result = await RunHubAsync(["--help"]);

        Assert.Equal(0, result.ExitCode);
        foreach (var (name, descriptionPrefix) in ExpectedCommandDescriptions)
        {
            Assert.Contains(name, result.StdOut, StringComparison.Ordinal);
            Assert.Contains(descriptionPrefix, result.StdOut, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ShortFlag_BehavesIdenticallyToHelp()
    {
        var result = await RunHubAsync(["-h"]);

        Assert.False(result.TimedOut, "The -h invocation must not hang waiting on app.Run().");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Now listening on:", result.StdOut, StringComparison.Ordinal);
        AssertUsageListsAllSwitches(result.StdOut);
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

    [Fact]
    public async Task SubmitSource_WithHelp_ShowsUsageInsteadOfSubmitting()
    {
        // FR-004 / spec.md edge case: --help always wins over submit-source, even when
        // it appears after the command name.
        var result = await RunHubAsync(["submit-source", "--help"]);

        Assert.False(result.TimedOut, "submit-source --help must not hang waiting on a submission.");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Submitted ingest task:", result.StdOut, StringComparison.Ordinal);
        AssertUsageListsAllSwitches(result.StdOut);
    }

    /// <summary>
    /// T036 (018-hub-cli-commands, contracts/cli-commands.md "Help contract"): extends the
    /// "--help wins" pattern above to one of the new blocking commands — proving FR-011's
    /// precedence rule is not special-cased to submit-source.
    /// </summary>
    [Fact]
    public async Task LintRun_WithHelp_ShowsUsageInsteadOfTriggeringARun()
    {
        var result = await RunHubAsync(["lint-run", "--help"]);

        Assert.False(result.TimedOut, "lint-run --help must not hang waiting on a run.");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Now listening on:", result.StdOut, StringComparison.Ordinal);
        // FR-006's status-line vocabulary ("Lint run {id} started.") never appears — no
        // run was ever triggered.
        Assert.DoesNotContain("started.", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain(FigletLogoMarker, result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// T036 (018-hub-cli-commands, contracts/cli-commands.md "Help contract"): a sample of
    /// per-command <c>--help</c> outputs (research.md D3/D7 — logo-free, that command's own
    /// arguments/options) across 3 of the 8 commands, picked for shape variety: no extra
    /// arguments beyond path switches (<c>lint-run</c>), a required positional argument plus
    /// two options (<c>query</c>), and a required named option (<c>remediation-authorize</c>).
    /// Not all 8 — the out-of-process spawn is comparatively slow, and each command's
    /// settings type is exercised far more thoroughly in-process by
    /// <c>HubCliCommandTests</c>/<c>HubCliQueryCommandTests</c>; this test's job is only to
    /// prove Spectre's help RENDERING (not parsing/execution) surfaces the right shape.
    /// </summary>
    [Theory]
    [InlineData("lint-run", new string[0])]
    [InlineData("query", new[] { "<prompt>", "--conversation-id", "--timeout" })]
    [InlineData("remediation-authorize", new[] { "--task-id" })]
    public async Task CommandHelp_ShowsItsOwnArguments_LogoFree_NoServerOptionsSection(string commandName, string[] expectedOwnArguments)
    {
        var result = await RunHubAsync([commandName, "--help"]);

        Assert.False(result.TimedOut, $"{commandName} --help must exit promptly.");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(commandName, result.StdOut, StringComparison.Ordinal);
        foreach (var expectedArgument in expectedOwnArguments)
        {
            Assert.Contains(expectedArgument, result.StdOut, StringComparison.Ordinal);
        }

        // Every path switch is still listed as an OPTION (HubPathSettings inheritance) —
        // just not under a separate "Server options:" section, and without the logo:
        // both are root-only per HubCliHelpProvider (research.md D3/D7).
        foreach (var pathSwitch in PathSwitchCatalog.All)
        {
            Assert.Contains(pathSwitch.Name, result.StdOut, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(FigletLogoMarker, result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Server options:", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// T036 (018-hub-cli-commands, contracts/cli-commands.md "Global rules": "Unknown
    /// first-argument command name → usage error naming the unknown command, exit 2").
    /// Re-verified after Phase 6's <c>--query</c>/dispatch changes: <c>ShouldDispatchToCli</c>
    /// (Program.cs) still routes any bareword first argument to the CommandApp, whose own
    /// unknown-command validation (not a catalog-name pre-check) produces this error.
    /// </summary>
    [Fact]
    public async Task UnknownCommandName_PrintsUsageError_NamingIt_ExitsTwo()
    {
        var result = await RunHubAsync(["no-such-command"]);

        Assert.False(result.TimedOut, "An unknown command name must fail fast, not hang.");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("no-such-command", result.StdOut + result.StdErr, StringComparison.Ordinal);
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
    /// <c>--help</c>, but for a real command invocation instead.
    /// </summary>
    [Fact]
    public async Task RemediationAuthorize_MissingRequiredTaskId_FailsValidation_NeverBindsAPortOrResolvesPaths()
    {
        var bogusPath = Path.Combine(Path.GetTempPath(), $"grimoire-help-bogus-{Guid.NewGuid():N}");

        var result = await RunHubAsync(["remediation-authorize", "--data-dir", bogusPath]);

        Assert.False(result.TimedOut, "A missing required option must fail validation promptly, not hang.");
        Assert.Equal(2, result.ExitCode);
        Assert.DoesNotContain("Now listening on:", result.StdOut, StringComparison.Ordinal);
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

    private static void AssertUsageListsAllSwitches(string stdOut)
    {
        foreach (var expected in ExpectedSwitches)
        {
            Assert.Contains(expected, stdOut, StringComparison.Ordinal);
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
