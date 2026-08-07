using System.Diagnostics;
using Grimoire.EvalRunner.Workspace;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// SC-001 (US1) — the hub completes successfully when invoked with no command-line flags
/// and no environment variables, against the versioned <c>appsettings.json</c> and a real
/// built agent directory, resolving every location from the config-file tier alone. Spawns
/// the actual built <c>Grimoire.Hub</c> executable — the same out-of-process pattern as
/// <see cref="HubHelpUsageTests"/> — because an in-process host (<c>WebApplicationFactory</c>)
/// could never observe "no flags, no env vars" the way a real process launch does.
/// </summary>
public class ZeroConfigStartupTests
{
    [Fact]
    public async Task NoFlagsNoEnvVars_CompletesSuccessfully_ResolvingEveryLocationFromConfigFile()
    {
        var repoRoot = EvalPaths.Discover().RepoRoot;
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-zero-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);

        try
        {
            // A real, complete agent directory at the documented default relative
            // location (.grimoire/agents) — produced by the repo's own solution build
            // (PublishAgentRuntime), copied here rather than pointed at via a switch,
            // because SC-001 tests the true zero-flag path.
            var realAgentDir = Path.Combine(repoRoot, ".grimoire", "agents");
            Assert.True(
                Directory.Exists(realAgentDir),
                $"Expected a built agent directory at {realAgentDir} — run `dotnet build backend/Grimoire.slnx` first.");
            CopyDirectory(realAgentDir, Path.Combine(cwd, ".grimoire", "agents"));
            File.WriteAllText(Path.Combine(cwd, ".env"), "ANTHROPIC_AUTH_TOKEN=test-token\n");

            // A CLI command that fully resolves and validates every path (including the
            // whole agent runtime) but exits promptly without spawning any agent —
            // "no flags" means no PATH switches; --task-id is the command's own argument,
            // not path configuration (mirrors HubHelpUsageTests's identical real-dispatch
            // idiom for exactly this reason).
            var result = await RunHubAsync(cwd, args: ["remediation-dismiss", "--task-id", "does-not-exist"], clearGrimoirePathsEnvVars: true);

            // SC-001: the command completes successfully — reaching the command's own
            // "not found" outcome (CliExitCode.NotFound) proves path resolution and agent-
            // runtime validation both succeeded first; a path failure would exit non-zero
            // before the command body ever runs (data-model.md §5's fail-before-validate
            // ordering). The structured paths_resolved event itself is OTLP-exported, not
            // written to console, so it is not observable from captured stdout/stderr here.
            Assert.False(result.TimedOut, "A no-flags, no-env-vars invocation must exit promptly, not hang.");
            Assert.Equal(3, result.ExitCode); // CliExitCode.NotFound
            Assert.Equal("Remediation task 'does-not-exist' was not found.", result.StdOut.Trim());

            // FR-010: the runtime data and wiki directories were created automatically —
            // both resolved from appsettings.json's default relative values (.grimoire,
            // llm-wiki) since no --data-dir/--wiki-dir/env-var was set (SC-001).
            Assert.True(Directory.Exists(Path.Combine(cwd, ".grimoire")));
            Assert.True(Directory.Exists(Path.Combine(cwd, "llm-wiki")));
        }
        finally
        {
            if (Directory.Exists(cwd))
            {
                Directory.Delete(cwd, recursive: true);
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }
    }

    private static async Task<HubRunResult> RunHubAsync(string workingDirectory, IReadOnlyList<string> args, bool clearGrimoirePathsEnvVars)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (clearGrimoirePathsEnvVars)
        {
            // Belt-and-braces: strip any Grimoire__Paths__* the ambient environment
            // (e.g. a developer's shell) might already have set, so this genuinely
            // exercises the zero-configuration path.
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                var key = (string)entry.Key;
                if (key.StartsWith("Grimoire__Paths__", StringComparison.Ordinal))
                {
                    startInfo.Environment.Remove(key);
                }
            }
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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

    /// <summary>Mirrors <see cref="HubHelpUsageTests"/>'s identically-named helper.</summary>
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
