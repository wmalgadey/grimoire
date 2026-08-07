using System.Diagnostics;
using Grimoire.EvalRunner.Workspace;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// SC-008/FR-011 (US4) — a real <c>dotnet build</c> invocation redirected via
/// <c>-p:GrimoireAgentDir=&lt;temp&gt;</c> delivers a complete, launchable agent runtime
/// for every agent type; rebuilding after an instruction-source edit refreshes the copy;
/// a stale leftover file in the destination is gone after the next build (clear-then-copy,
/// <c>backend/Directory.Build.targets</c>'s <c>PublishAgentRuntime</c> target).
/// </summary>
public class AgentDirBuildContractTests
{
    private static readonly (string AgentId, string ProjectDir, string WorkerFileName)[] AgentTypes =
    [
        ("ingest", "Grimoire.IngestAgent", "Grimoire.IngestAgent.dll"),
        ("query", "Grimoire.QueryAgent", "Grimoire.QueryAgent.dll"),
        ("lint", "Grimoire.LintAgent", "Grimoire.LintAgent.dll"),
    ];

    [Fact]
    public async Task BuildingWithCustomAgentDir_DeliversCompleteByteMatchingLaunchableRuntimes_ForEveryAgentType()
    {
        var repoRoot = EvalPaths.Discover().RepoRoot;
        var tempAgentDir = Path.Combine(Path.GetTempPath(), $"grimoire-agentdir-build-{Guid.NewGuid():N}") + Path.DirectorySeparatorChar;

        try
        {
            await RunDotnetBuildAsync(repoRoot, Path.Combine(repoRoot, "backend", "Grimoire.slnx"), tempAgentDir);

            foreach (var (agentId, projectDir, workerFileName) in AgentTypes)
            {
                var destDir = Path.Combine(tempAgentDir, agentId);

                Assert.True(File.Exists(Path.Combine(destDir, workerFileName)), $"Expected {workerFileName} under {destDir}.");
                Assert.True(
                    File.Exists(Path.Combine(destDir, Path.GetFileNameWithoutExtension(workerFileName) + ".deps.json")),
                    $"Expected a deps.json under {destDir}.");
                Assert.True(
                    File.Exists(Path.Combine(destDir, Path.GetFileNameWithoutExtension(workerFileName) + ".runtimeconfig.json")),
                    $"Expected a runtimeconfig.json under {destDir}.");

                var sourceInstructionsDir = Path.Combine(repoRoot, "backend", "src", projectDir, "Instructions");
                var destInstructionsDir = Path.Combine(destDir, "Instructions");
                Assert.True(Directory.Exists(destInstructionsDir), $"Expected {destInstructionsDir} to exist.");

                foreach (var sourceFile in Directory.GetFiles(sourceInstructionsDir))
                {
                    var destFile = Path.Combine(destInstructionsDir, Path.GetFileName(sourceFile));
                    Assert.True(File.Exists(destFile), $"Expected {destFile} to exist (source: {sourceFile}).");
                    Assert.Equal(await File.ReadAllTextAsync(sourceFile), await File.ReadAllTextAsync(destFile));
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempAgentDir))
            {
                Directory.Delete(tempAgentDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RebuildingAfterInstructionEdit_Refreshes_AndClearsAStaleLeftoverFile_AndIsGenuinelyLaunchable()
    {
        var repoRoot = EvalPaths.Discover().RepoRoot;
        var tempAgentDir = Path.Combine(Path.GetTempPath(), $"grimoire-agentdir-refresh-{Guid.NewGuid():N}") + Path.DirectorySeparatorChar;
        var ingestProject = Path.Combine(repoRoot, "backend", "src", "Grimoire.IngestAgent", "Grimoire.IngestAgent.csproj");
        var systemPromptSource = Path.Combine(repoRoot, "backend", "src", "Grimoire.IngestAgent", "Instructions", "system-prompt.md");
        var originalSystemPrompt = await File.ReadAllTextAsync(systemPromptSource);

        try
        {
            await RunDotnetBuildAsync(repoRoot, ingestProject, tempAgentDir);
            var destSystemPrompt = Path.Combine(tempAgentDir, "ingest", "Instructions", "system-prompt.md");
            Assert.Equal(originalSystemPrompt, await File.ReadAllTextAsync(destSystemPrompt));

            // FR-011: touching an instruction source and rebuilding refreshes the delivered copy.
            var touched = originalSystemPrompt + "\n<!-- T035 refresh probe -->\n";
            await File.WriteAllTextAsync(systemPromptSource, touched);
            await RunDotnetBuildAsync(repoRoot, ingestProject, tempAgentDir);
            Assert.Equal(touched, await File.ReadAllTextAsync(destSystemPrompt));

            // Restore the source before the next assertions so a failure here doesn't
            // leave the repo's tracked instruction file permanently mutated.
            await File.WriteAllTextAsync(systemPromptSource, originalSystemPrompt);

            // Clear-then-copy: a stale leftover file in the destination is gone after the
            // next build, not merged with the fresh output.
            var staleFile = Path.Combine(tempAgentDir, "ingest", "stale-leftover.txt");
            await File.WriteAllTextAsync(staleFile, "stale");
            await RunDotnetBuildAsync(repoRoot, ingestProject, tempAgentDir);
            Assert.False(File.Exists(staleFile), "Expected the next build's clear-then-copy to remove a stale leftover file.");
            Assert.Equal(originalSystemPrompt, await File.ReadAllTextAsync(destSystemPrompt));

            // The delivered directory is genuinely launchable: the worker starts and
            // fails on its own missing-argument validation, never on assembly resolution.
            var workerPath = Path.Combine(tempAgentDir, "ingest", "Grimoire.IngestAgent.dll");
            var (_, _, stderr) = await RunProcessAsync("dotnet", [workerPath, "--help"], TimeSpan.FromSeconds(20));
            Assert.DoesNotContain("Could not load file or assembly", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("FileNotFoundException", stderr, StringComparison.Ordinal);
        }
        finally
        {
            if (await File.ReadAllTextAsync(systemPromptSource) != originalSystemPrompt)
            {
                await File.WriteAllTextAsync(systemPromptSource, originalSystemPrompt);
            }
            if (Directory.Exists(tempAgentDir))
            {
                Directory.Delete(tempAgentDir, recursive: true);
            }
        }
    }

    private static async Task RunDotnetBuildAsync(string repoRoot, string projectOrSolutionPath, string agentDir)
    {
        // -nodeReuse:false: without it, MSBuild's persistent worker nodes outlive this
        // `dotnet build` invocation, and — since child processes inherit this process's
        // file descriptors on Linux — a surviving node can keep the redirected stdout/
        // stderr pipe's write end open indefinitely, hanging ReadToEndAsync forever even
        // after `dotnet build` itself has exited (see RunProcessAsync's file-redirection
        // workaround below for the same reason).
        var (exitCode, stdout, stderr) = await RunProcessAsync(
            "dotnet",
            ["build", projectOrSolutionPath, $"-p:GrimoireAgentDir={agentDir}", "-nodeReuse:false"],
            TimeSpan.FromMinutes(3),
            workingDirectory: repoRoot);

        Assert.True(exitCode == 0, $"dotnet build of {projectOrSolutionPath} failed (exit {exitCode}).\nstdout:\n{stdout}\nstderr:\n{stderr}");
    }

    /// <summary>
    /// Redirects the child's stdout/stderr to real files via a shell, instead of via
    /// <see cref="Process.StandardOutput"/>/<see cref="ProcessStartInfo.RedirectStandardOutput"/>
    /// pipes read with <c>ReadToEndAsync</c>: a pipe only signals EOF once every process
    /// holding its write end has closed it, and a lingering MSBuild worker node that
    /// inherited the pipe (Linux fork/exec semantics) can keep it open long after the
    /// direct child has exited, hanging the read forever. Reading a file back after the
    /// process exits has no such dependency on which processes still hold a handle open.
    /// </summary>
    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName, IReadOnlyList<string> args, TimeSpan timeout, string? workingDirectory = null)
    {
        var stdOutPath = Path.Combine(Path.GetTempPath(), $"grimoire-proc-out-{Guid.NewGuid():N}.log");
        var stdErrPath = Path.Combine(Path.GetTempPath(), $"grimoire-proc-err-{Guid.NewGuid():N}.log");

        try
        {
            var quotedArgs = string.Join(' ', args.Select(a => "'" + a.Replace("'", "'\\''") + "'"));
            var shellCommand = $"exec {fileName} {quotedArgs} > '{stdOutPath}' 2> '{stdErrPath}'";

            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(shellCommand);
            if (workingDirectory is not null)
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");

            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Already exited between timeout and kill.
                }
                throw new TimeoutException($"'{fileName} {string.Join(' ', args)}' did not exit within {timeout}.");
            }

            var stdOut = File.Exists(stdOutPath) ? await File.ReadAllTextAsync(stdOutPath) : string.Empty;
            var stdErr = File.Exists(stdErrPath) ? await File.ReadAllTextAsync(stdErrPath) : string.Empty;
            return (process.ExitCode, stdOut, stdErr);
        }
        finally
        {
            File.Delete(stdOutPath);
            File.Delete(stdErrPath);
        }
    }
}
