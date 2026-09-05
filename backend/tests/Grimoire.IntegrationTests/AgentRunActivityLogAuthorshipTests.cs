using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// 025-agent-owned-log T017/T018 (FR-001, FR-002, FR-007, SC-002): a run that changes
/// nothing leaves the activity log exactly as it found it, because nothing in the harness
/// writes the file any more.
///
/// These are run-level companions to the structural guarantee. Boundary Rule BR-1 proves
/// no agent-assembly code can reach a filesystem-write API outside the guarded layer;
/// these prove the observable consequence on the two paths the deleted backstop used to
/// fire on — a run that failed, and a turn that wrote nothing. Real agent loop, real
/// guarded executor, real files in a per-test temp directory; the only double is
/// <see cref="FakeModelClient"/>, the existing hand-rolled fake for the
/// <c>IModelClient</c> port (Constitution Principle II).
/// </summary>
public class AgentRunActivityLogAuthorshipTests
{
    private const string SeededLog =
        "## [2026-08-01] ingest | created retrieval-patterns\n\n" +
        "Created [[concepts/retrieval-patterns]] from source \"notes.md\". Task: task-earlier.\n";

    private const string SourceContent = "# Source\n\nSome content to integrate.\n";

    /// <summary>
    /// FR-002, SC-002: the failure path the backstop used to write on unconditionally
    /// (<c>forceAppend: true</c>). The run throws after touching nothing; the log is
    /// untouched and carries no harness-authored text.
    /// </summary>
    [Fact]
    public async Task FailedIngestRun_LeavesActivityLogUnchanged_AndWritesNoFallbackEntry()
    {
        var (root, wikiDir, logPath) = CreateWorkspace();
        try
        {
            await File.WriteAllTextAsync(logPath, SeededLog);
            var seededBytes = await File.ReadAllBytesAsync(logPath);

            // A model that fails before any write happens.
            var fake = new FakeModelClient(new List<ModelTurn>());
            var executor = NewExecutor(root, wikiDir, logPath);
            var loop = new AgentLoop(fake, executor);

            await Assert.ThrowsAnyAsync<Exception>(() => loop.RunIngestSourceAsync(
                systemPrompt: "You are a test agent.",
                userPrompt: "Integrate the source.",
                taskId: "task-failing",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None));

            Assert.Equal(seededBytes, await File.ReadAllBytesAsync(logPath));

            var content = await File.ReadAllTextAsync(logPath);
            Assert.DoesNotContain("harness backstop", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("reconciled on startup", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// FR-007, SC-002: a turn that answers without writing any wiki content. The log is
    /// byte-for-byte unchanged — the changes-only criterion, observed at run level.
    /// </summary>
    [Fact]
    public async Task NoWriteRun_LeavesActivityLogUnchanged()
    {
        var (root, wikiDir, logPath) = CreateWorkspace();
        try
        {
            await File.WriteAllTextAsync(logPath, SeededLog);
            var seededBytes = await File.ReadAllBytesAsync(logPath);

            // A routine lookup: read something, then answer. No write_file at all.
            var fake = new FakeModelClient(new List<ModelTurn>
            {
                FakeModelClient.ReadFileTurn("t1", "wiki/log.md"),
                FakeModelClient.FinalTurn("Answered from what the wiki already knows."),
            });
            var executor = NewExecutor(root, wikiDir, logPath);
            var loop = new AgentLoop(fake, executor);

            var result = await loop.RunIngestSourceAsync(
                systemPrompt: "You are a test agent.",
                userPrompt: "What do we know about retrieval patterns?",
                taskId: "turn-no-write",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None);

            Assert.NotNull(result);
            Assert.Empty(executor.TouchedPaths);
            Assert.Equal(seededBytes, await File.ReadAllBytesAsync(logPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The complement, and the reason the two assertions above are not vacuous: when the
    /// agent itself writes the log through the guarded tool, the entry does land — and it
    /// lands on top, with the seeded content preserved as an exact suffix (FR-003).
    /// </summary>
    [Fact]
    public async Task AgentWrittenEntry_LandsOnTop_WithPriorContentPreserved()
    {
        var (root, wikiDir, logPath) = CreateWorkspace();
        try
        {
            await File.WriteAllTextAsync(logPath, SeededLog);

            const string newEntry =
                "## [2026-08-17] ingest | created hybrid-search\n\n" +
                "Created [[concepts/hybrid-search]] from source \"search.md\". Task: task-new.\n";

            var fake = new FakeModelClient(new List<ModelTurn>
            {
                FakeModelClient.ReadFileTurn("t1", "wiki/log.md"),
                FakeModelClient.WriteFileTurn("t2", "wiki/log.md", newEntry + SeededLog),
                FakeModelClient.FinalTurn("Logged the change."),
            });
            var executor = NewExecutor(root, wikiDir, logPath);
            var loop = new AgentLoop(fake, executor);

            await loop.RunIngestSourceAsync(
                systemPrompt: "You are a test agent.",
                userPrompt: "Integrate the source.",
                taskId: "task-logging",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None);

            var committed = await File.ReadAllTextAsync(logPath);
            Assert.StartsWith(newEntry, committed, StringComparison.Ordinal);
            Assert.EndsWith(SeededLog, committed, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (string Root, string WikiDir, string LogPath) CreateWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-log-authorship-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        Directory.CreateDirectory(Path.Combine(wikiDir, "tech"));
        return (root, wikiDir, Path.Combine(wikiDir, "log.md"));
    }

    private static GuardedToolExecutor NewExecutor(string root, string wikiDir, string logPath)
    {
        var policy = new SafetyPolicy(
            root,
            readPrefixes: [wikiDir + Path.DirectorySeparatorChar],
            writePrefixes:
            [
                Path.Combine(wikiDir, "tech") + Path.DirectorySeparatorChar,
                Path.Combine(wikiDir, "index.md"),
                logPath,
            ]);

        return new GuardedToolExecutor(
            policy,
            new WriteJournal(),
            root,
            taskId: "task-authorship",
            writeLocksDir: Path.Combine(root, "write-locks"),
            logPath: logPath);
    }
}
