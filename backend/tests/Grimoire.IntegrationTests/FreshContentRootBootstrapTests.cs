using Grimoire.Domain.Guardrails;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T047 (022-align-wiki-structure, US2, SC-013, FR-013): a content root that has neither
/// <c>index.md</c> nor <c>log.md</c> yet is left with both present and populated after an
/// ingest run, with no separate operator setup step. This is a confirmation/regression test
/// for behavior the harness already provides — ADR-017's format guard exempts "the file
/// does not yet exist and this is the first write" for both files (research.md R7) — the
/// spec change is that the ingest prompt now says so explicitly rather than leaving a
/// fresh content root's first-write path implicit.
/// </summary>
public class FreshContentRootBootstrapTests
{
    private const string SourceContent = "# Test Source\n\nSome test content about a technology.";

    [Fact]
    public async Task IngestRun_AgainstContentRootMissingIndexAndLog_LeavesBothPresentAndPopulated()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"fresh-root-bootstrap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var wikiDir = Path.Combine(tempRoot, "wiki");
            Directory.CreateDirectory(wikiDir);
            var indexPath = Path.Combine(wikiDir, "index.md");
            var logPath = Path.Combine(wikiDir, "log.md");

            // Neither index.md nor log.md exists yet — a genuinely fresh content root.
            Assert.False(File.Exists(indexPath));
            Assert.False(File.Exists(logPath));

            const string articleContent =
                "---\ntype: Technology\ntitle: Example Technology\ndescription: A technology used for testing.\n" +
                "timestamp: 2026-07-14T00:00:00Z\ntags:\n  - tech/ExampleTech\nconfidence: medium\n" +
                "confidence_reason: One source.\n---\n\nBody text about the example technology.\n";
            const string indexContent =
                "# Wiki Index\n\n## Tech\n\n" +
                "- [Example Technology](tech/example-technology.md) — A technology used for testing — Stub — keine Quellen\n";
            const string logContent =
                "## [2026-08-10] ingest | created example-technology\n\n" +
                "Created [[tech/example-technology]] from a test source. Task: [[tasks/task-fresh-root.md]].\n";

            var turns = new[]
            {
                FakeModelClient.WriteFileTurn("t1", "wiki/tech/example-technology.md", articleContent),
                FakeModelClient.WriteFileTurn("t2", "wiki/index.md", indexContent),
                FakeModelClient.WriteFileTurn("t3", "wiki/log.md", logContent),
                FakeModelClient.FinalTurn("Created Example Technology; bootstrapped index.md and log.md."),
            };

            var fake = new FakeModelClient(turns);
            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: [wikiDir + Path.DirectorySeparatorChar],
                writePrefixes: [wikiDir + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(
                policy, journal, tempRoot, taskId: "task-fresh-root",
                writeLocksDir: Path.Combine(tempRoot, "write-locks"),
                indexPath: indexPath,
                logPath: logPath);
            var loop = new AgentLoop(fake, executor);

            var result = await loop.RunAsync(
                systemPrompt: "Test ingest agent.",
                userPrompt: "Integrate the source.",
                taskId: "task-fresh-root",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(4, result.TurnsUsed);
            Assert.Empty(executor.Denials);

            Assert.True(File.Exists(indexPath), "index.md must be created as part of the first write to it.");
            Assert.True(File.Exists(logPath), "log.md must be created as part of the first write to it.");

            var onDiskIndex = await File.ReadAllTextAsync(indexPath);
            var onDiskLog = await File.ReadAllTextAsync(logPath);

            Assert.False(string.IsNullOrWhiteSpace(onDiskIndex), "index.md must be populated, not just created empty.");
            Assert.False(string.IsNullOrWhiteSpace(onDiskLog), "log.md must be populated, not just created empty.");

            Assert.Contains("[Example Technology](tech/example-technology.md)", onDiskIndex);
            Assert.Matches(@"^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$", onDiskLog.TrimEnd('\n').Split('\n')[0]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
