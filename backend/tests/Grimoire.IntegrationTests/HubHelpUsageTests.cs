using System.Diagnostics;
using Grimoire.EvalRunner.Workspace;
using Grimoire.Hub.Runtime.Paths;

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
    // own options, which have no equivalent catalog to source from.
    private static readonly string[] ExpectedSwitches =
    [
        .. PathSwitchCatalog.All.Select(s => s.Name),
        "submit-source",
        "--path",
        "--source-kind",
    ];

    [Fact]
    public async Task Help_PrintsUsage_ExitsZeroPromptly_AndNeverStartsTheWebServer()
    {
        var result = await RunHubAsync(["--help"]);

        Assert.False(result.TimedOut, "The --help invocation must exit promptly instead of starting the web host.");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Now listening on:", result.StdOut, StringComparison.Ordinal);
        AssertUsageListsAllSwitches(result.StdOut);
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
    public async Task Help_CombinedWithBogusBaseDir_StillWinsAndExitsZero()
    {
        // FR-004: --help must win before any path resolution is attempted against this
        // (deliberately nonexistent) --base-dir value — proven by the process exiting
        // promptly rather than failing on/creating the bogus path.
        var bogusPath = Path.Combine(Path.GetTempPath(), $"grimoire-help-bogus-{Guid.NewGuid():N}");

        var result = await RunHubAsync(["--help", "--base-dir", bogusPath]);

        Assert.False(result.TimedOut, "--help combined with other args must still exit promptly.");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Now listening on:", result.StdOut, StringComparison.Ordinal);
        Assert.False(Directory.Exists(bogusPath), "No path resolution against the bogus --base-dir may be attempted.");
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

    private static void AssertUsageListsAllSwitches(string stdOut)
    {
        foreach (var expected in ExpectedSwitches)
        {
            Assert.Contains(expected, stdOut, StringComparison.Ordinal);
        }
    }

    private static async Task<HubRunResult> RunHubAsync(IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
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
        _ = await stdErrTask;

        return new HubRunResult(timedOut ? -1 : process.ExitCode, stdOut, timedOut);
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

    private readonly record struct HubRunResult(int ExitCode, string StdOut, bool TimedOut);
}
