using Grimoire.Hub.QueryConversations;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IngestAgent.TaskArtifact;
using Grimoire.IntegrationTests.PathConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T025/T026 (014-wiki-storage-restructure, US2) — quickstart.md Scenario 2: against a
/// fresh base directory, a real task-artifact write (<see cref="TaskArtifactStore"/>) and a
/// real Conversation Record append (<see cref="ConversationRecordStore"/>) both land under
/// base-level sibling directories, neither nested inside the wiki content root nor the
/// internal data directory (SC-002); the internal data directory's remaining locations
/// (raw intake, operational state, secrets, agent instructions/policy, write-locks,
/// findings) stay anchored exactly where they were before this feature (FR-005).
/// </summary>
public class SiblingDirectoryLayoutTests
{
    [Fact]
    public async Task TaskAndConversationCreation_LandUnderBaseLevelSiblings_NotNestedInWikiOrData()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-sibling-layout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();
            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            // Trigger a task artifact write via the same store production code uses
            // (Grimoire.IngestAgent.Program.cs) — a harness-owned direct file I/O, not an
            // agent tool call (R4).
            var taskId = "sibling-layout-task-1";
            var taskStore = new TaskArtifactStore();
            var taskArtifactPath = resolved.TaskArtifactPathFor(taskId);
            await taskStore.WriteAsync(
                taskArtifactPath,
                new TaskArtifactDocument(
                    TaskId: taskId,
                    Type: "ingest",
                    Status: "completed",
                    Agent: "ingest",
                    StartedAt: DateTimeOffset.UtcNow,
                    CompletedAt: DateTimeOffset.UtcNow,
                    SourceRef: "https://example.com/source",
                    PagesTouched: [],
                    FailureReason: null,
                    Narrative: "Sibling-layout regression fixture."),
                CancellationToken.None);

            // Trigger a conversation-record append via the same store production code uses
            // (Grimoire.Hub.QueryDispatch.QueryRunCoordinator).
            var conversationId = "sibling-layout-conversation-1";
            var conversationStore = new ConversationRecordStore(resolved);
            await conversationStore.AppendTurnAsync(
                conversationId,
                new RecordedTurn(
                    TurnId: "t-1",
                    Position: 1,
                    State: "completed",
                    FailureReason: null,
                    StartedAt: DateTimeOffset.UtcNow,
                    CompletedAt: DateTimeOffset.UtcNow,
                    Model: "claude-sonnet-4-5",
                    TurnsUsed: 1,
                    InstructionFilePath: "agents/query/system-prompt.md",
                    InstructionFileSha256: "sha-1",
                    PolicyPath: "agents/query/policy.json",
                    PolicyVersion: 1,
                    PolicySha256: "sha-2",
                    DeniedActions: [],
                    Prompt: "What is the sibling layout?",
                    Answer: "Tasks and conversations sit beside the wiki."),
                CancellationToken.None);

            var conversationRecordPath = resolved.ConversationRecordPathFor(conversationId);

            // Both artifacts actually landed on disk...
            Assert.True(File.Exists(taskArtifactPath));
            Assert.True(File.Exists(conversationRecordPath));

            // ...directly under base-level sibling directories...
            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "tasks", $"{taskId}.md")), taskArtifactPath);
            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "conversations", $"{conversationId}.md")), conversationRecordPath);

            // ...neither nested inside the wiki content root...
            Assert.DoesNotContain(resolved.ContentRoot, taskArtifactPath, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.ContentRoot, conversationRecordPath, StringComparison.Ordinal);

            // ...nor inside the internal data directory.
            Assert.DoesNotContain(resolved.DataDir, taskArtifactPath, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.DataDir, conversationRecordPath, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// FR-005 regression: this feature relocates only <c>TasksDir</c>/<c>ConversationsDir</c>
    /// (US1/US2) — every other internal runtime-only data location (raw intake, operational
    /// state, secrets, agent instructions/policy, write-locks, findings) still resolves
    /// beneath <see cref="ResolvedGrimoirePaths.DataDir"/>, exactly as it did before this
    /// feature (data-model.md "DataDir and everything under it ... Unchanged (FR-005)").
    /// </summary>
    [Fact]
    public void DataDirLocations_StayAnchoredBeneathDataDir_UnaffectedByTheSiblingRelocation()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-sibling-layout-datadir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();
            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            string DataRelative(string path)
            {
                Assert.StartsWith(resolved.DataDir + Path.DirectorySeparatorChar, path, StringComparison.Ordinal);
                return Path.GetRelativePath(resolved.DataDir, path);
            }

            Assert.Equal(Path.Combine("raw", "originals"), DataRelative(resolved.RawOriginalsDir));
            Assert.Equal(Path.Combine("raw", "sources"), DataRelative(resolved.RawSourcesDir));
            Assert.Equal(Path.Combine("state", "operational-state.db"), DataRelative(resolved.StateDbPath));
            Assert.Equal(".env", DataRelative(resolved.SecretsFilePath));
            Assert.Equal(Path.Combine("agents", "ingest"), DataRelative(resolved.InstructionsDir));
            Assert.Equal(Path.Combine("agents", "query"), DataRelative(resolved.QueryInstructionsDir));
            Assert.Equal(Path.Combine("agents", "lint"), DataRelative(resolved.LintInstructionsDir));
            Assert.Equal("write-locks", DataRelative(resolved.WriteLocksDir));
            Assert.Equal("findings", DataRelative(resolved.FindingsDir));

            // Neither of the two relocated siblings lives under DataDir any more.
            Assert.DoesNotContain(resolved.DataDir, resolved.TasksDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.DataDir, resolved.ConversationsDir, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }
}
