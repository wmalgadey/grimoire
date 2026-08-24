using Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;
using Grimoire.Hub.IngestDispatch;

namespace Grimoire.IntegrationTests;

/// <summary>
/// Issue #183: every dispatch-path spawn (<c>AgentProcessHost.StartAsync</c>, the path
/// every real run uses — never the manual <c>submit-source</c> CLI's
/// <c>RunToExitAsync</c>) redirects the child's stderr (<c>RedirectStandardError =
/// true</c>) but, before this fix, nothing ever read it. A Linux pipe holds 64 KiB by
/// default; once it fills with nobody draining it, the child's next stderr write blocks
/// forever, wedging the run — indistinguishable from a healthy process doing real work.
///
/// This spawns a REAL separate process (<c>Grimoire.WriteLockTestHarness</c>, standing in
/// for the ingest agent worker via <c>AgentProcessHost</c>'s exact production spawn path)
/// that writes ~200 KB to stderr — three times the default pipe buffer. An in-process fake
/// cannot prove this: the deadlock is a genuine OS pipe-buffer phenomenon, only
/// reproducible against a real child process and a real unread pipe.
/// </summary>
public class IngestAgentProcessDispatchStderrDrainTests
{
    private static string HarnessDllPath =>
        Path.Combine(AppContext.BaseDirectory, "Grimoire.WriteLockTestHarness.dll");

    [Fact]
    public async Task TalkativeAgent_WritingPastThePipeBuffer_StillReachesItsTerminalEvent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"stderr-drain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var loader = new LocalSecretsLoader(Path.Combine(root, ".env"));
            // The harness stands in for all three worker paths — this test only spawns
            // the Ingest overload, so the Query/Lint paths are never exercised.
            var processHost = new AgentProcessHost(loader, HarnessDllPath, HarnessDllPath, HarnessDllPath);

            var handle = await processHost.StartAsync(new IngestAgentRequest(
                TaskId: $"test-{Guid.NewGuid():N}",
                SourceRef: "unused",
                SourceKind: "file",
                WikiRoot: root,
                ContentRoot: root,
                TasksDir: root,
                IndexPath: Path.Combine(root, "index.md"),
                LogPath: Path.Combine(root, "log.md"),
                PastedText: null,
                SystemPromptPath: Path.Combine(root, "system-prompt.md"),
                DefaultUserPromptPath: Path.Combine(root, "default-user-prompt.md"),
                PolicyPath: Path.Combine(root, "policy.json"),
                WriteLocksDir: Path.Combine(root, "write-locks")));

            var stdoutLines = new List<string>();
            try
            {
                // Before the fix this loop hangs forever: the harness process blocks on
                // its stderr write past the pipe's 64 KiB buffer (nothing drains it) and
                // so never exits, never closes stdout, and this read never returns null.
                // Bounding it here turns that hang into a failing test instead of a CI
                // job that never completes.
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await foreach (var line in handle.ReadStdoutLinesAsync(cts.Token))
                {
                    stdoutLines.Add(line);
                }
            }
            finally
            {
                await handle.DisposeAsync();
            }

            Assert.Contains(stdoutLines, line => line.Contains("\"status\":\"completed\"", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
