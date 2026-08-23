using System.Diagnostics;
using Grimoire.EvalRunner.Workspace;

namespace Grimoire.IntegrationTests;

/// <summary>
/// The eval runner's argument contract: a wrong argument fails the run and names itself,
/// rather than being skipped.
///
/// Written from a real incident. The parser walked <c>args</c> in fixed pairs and ignored
/// anything it did not recognize, so <c>capture --no-build --scenario lint-defects-found</c>
/// — one <c>dotnet run</c> flag on the wrong side of the <c>--</c> — shifted every
/// following option onto an odd index where nothing matched. The result was an empty
/// scenario filter, and an empty filter means EVERY scenario: a seven-scenario refresh
/// became a live re-capture of the whole corpus against the provider, with no warning at
/// any point. The selection-widening is the damage, so that is what these tests assert —
/// through the real built binary, spawned the way an operator or a workflow invokes it.
///
/// <c>status</c> is the vehicle throughout: it reads recordings, needs no provider
/// credentials, and writes the same scenario selection to <c>--summary</c> that
/// <c>capture</c> would have run live.
/// </summary>
public class EvalRunnerCliArgumentTests : IDisposable
{
    private const int UsageErrorExitCode = 2;

    private readonly string _summaryPath = Path.Combine(
        Path.GetTempPath(), $"grimoire-evalrunner-summary-{Guid.NewGuid():N}.md");

    public void Dispose()
    {
        if (File.Exists(_summaryPath))
        {
            File.Delete(_summaryPath);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task StrayFlag_FailsAndNamesIt_InsteadOfSilentlySelectingEveryScenario()
    {
        var result = await RunEvalRunnerAsync(
            ["status", "--no-build", "--scenario", "update-over-duplicate", "--summary", _summaryPath]);

        Assert.Equal(UsageErrorExitCode, result.ExitCode);
        Assert.Contains("Unrecognized argument '--no-build'.", result.StdErr, StringComparison.Ordinal);
        Assert.False(
            File.Exists(_summaryPath),
            "A rejected argument list must produce no scenario run at all — the pre-fix parser instead "
                + "dropped the --scenario filter and selected every scenario.");
    }

    [Fact]
    public async Task ValidArguments_SelectOnlyTheRequestedScenario()
    {
        // The positive control for the test above: with the same arguments minus the stray
        // flag, exactly one scenario is selected. This is the assertion the pre-fix parser
        // failed — it reported all 27.
        var result = await RunEvalRunnerAsync(
            ["status", "--scenario", "update-over-duplicate", "--summary", _summaryPath]);

        Assert.False(result.TimedOut, "A status run over one scenario must exit promptly.");
        Assert.True(File.Exists(_summaryPath), $"No summary was written. stderr: {result.StdErr}");

        var summary = await File.ReadAllTextAsync(_summaryPath);
        var scenarioRows = summary
            .Split('\n')
            .Count(line => line.Contains("| status:", StringComparison.Ordinal));
        Assert.Equal(1, scenarioRows);
        Assert.Contains("status:update-over-duplicate", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MisspelledScenarioId_FailsAndNamesIt_InsteadOfRunningAShorterSelection()
    {
        var result = await RunEvalRunnerAsync(
            ["status", "--scenario", "update-over-duplicate", "--scenario", "lint-defcts-found", "--summary", _summaryPath]);

        Assert.Equal(UsageErrorExitCode, result.ExitCode);
        Assert.Contains("Unknown scenario id(s): lint-defcts-found.", result.StdErr, StringComparison.Ordinal);
        Assert.False(
            File.Exists(_summaryPath),
            "One unknown id must fail the whole invocation, not quietly run the remaining valid ids.");
    }

    [Fact]
    public async Task OptionWithoutAValue_Fails()
    {
        var result = await RunEvalRunnerAsync(["status", "--summary", _summaryPath, "--scenario"]);

        Assert.Equal(UsageErrorExitCode, result.ExitCode);
        Assert.Contains("Option '--scenario' requires a value.", result.StdErr, StringComparison.Ordinal);
        Assert.False(File.Exists(_summaryPath));
    }

    [Fact]
    public async Task OptionFollowedByAnotherOption_Fails()
    {
        var result = await RunEvalRunnerAsync(["status", "--scenario", "--samples", "3"]);

        Assert.Equal(UsageErrorExitCode, result.ExitCode);
        Assert.Contains(
            "Option '--scenario' requires a value, but was followed by '--samples'.",
            result.StdErr,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonNumericSampleCount_Fails()
    {
        var result = await RunEvalRunnerAsync(["status", "--samples", "abc"]);

        Assert.Equal(UsageErrorExitCode, result.ExitCode);
        Assert.Contains("Option '--samples' requires an integer, got 'abc'.", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoSubcommand_Fails()
    {
        var result = await RunEvalRunnerAsync(["--scenario", "update-over-duplicate"]);

        Assert.Equal(UsageErrorExitCode, result.ExitCode);
        Assert.Contains(
            "Expected a subcommand as the first argument, got '--scenario'.",
            result.StdErr,
            StringComparison.Ordinal);
    }

    private static async Task<EvalRunnerResult> RunEvalRunnerAsync(IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(ResolveEvalRunnerDllPath(EvalPaths.Discover().RepoRoot));
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Grimoire.EvalRunner process.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
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

        return new EvalRunnerResult(
            timedOut ? -1 : process.ExitCode, await stdOutTask, await stdErrTask, timedOut);
    }

    /// <summary>
    /// Mirrors <see cref="AgentProcessInvoker.ResolveAgentDllPath"/>: the runner is launched
    /// from its OWN build output, never a copy in this test project's output directory.
    /// </summary>
    private static string ResolveEvalRunnerDllPath(string repoRoot)
    {
        var separator = Path.DirectorySeparatorChar;
        var preferred = AppContext.BaseDirectory.Contains($"{separator}Release{separator}", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Release", "Debug" }
            : ["Debug", "Release"];

        foreach (var configuration in preferred)
        {
            var candidate = Path.Combine(
                repoRoot, "backend", "src", "Grimoire.EvalRunner", "bin", configuration, "net10.0", "Grimoire.EvalRunner.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Grimoire.EvalRunner.dll not found in its build output. Build first: dotnet build backend/Grimoire.slnx");
    }

    private readonly record struct EvalRunnerResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);
}
