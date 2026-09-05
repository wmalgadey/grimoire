using Grimoire.Hub.QueryConversations;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IngestAgent.TaskArtifact;
using Grimoire.IntegrationTests.PathConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// ADR-022, amended by ADR-024 (US1/US2) — against a fresh checkout, a real task-artifact
/// write (<see cref="TaskArtifactStore"/>) and a real Conversation Record append
/// (<see cref="QueryConversationRecordStore"/>) both land under memory-directory siblings —
/// agent output (022-memory-directory-root FR-002) — never nested inside the wiki or
/// internal data directory; the data directory's own locations (raw intake, operational
/// state, agent runtime, write-locks) stay anchored exactly where the resolver puts them,
/// and the secrets file stays anchored at the process working directory regardless of any
/// root (FR-019). The reflection-driven <see cref="PathGroupingInvariantTests"/> (ADR-024
/// rule M5) covers the general relocation-matrix invariant; this file keeps the
/// byte-on-disk, real-store-write coverage that a pure reflection test cannot provide.
/// </summary>
public class SiblingDirectoryLayoutTests
{
    [Fact]
    public async Task TaskAndConversationCreation_LandUnderMemoryDirSiblings_NotNestedInWikiOrDataDir()
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
            var conversationStore = new QueryConversationRecordStore(resolved);
            await conversationStore.AppendTurnAsync(
                conversationId,
                new QueryRecordedTurn(
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

            // ...directly under memory-directory sibling directories (agent output, FR-002)...
            Assert.Equal(Path.GetFullPath(Path.Combine(resolved.MemoryDir, "tasks", $"{taskId}.md")), taskArtifactPath);
            Assert.Equal(Path.GetFullPath(Path.Combine(resolved.MemoryDir, "conversations", $"{conversationId}.md")), conversationRecordPath);

            // ...nested inside the memory directory...
            Assert.StartsWith(resolved.MemoryDir, taskArtifactPath, StringComparison.Ordinal);
            Assert.StartsWith(resolved.MemoryDir, conversationRecordPath, StringComparison.Ordinal);

            // ...never inside the wiki or the internal data directory.
            Assert.DoesNotContain(resolved.WikiDir, taskArtifactPath, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.WikiDir, conversationRecordPath, StringComparison.Ordinal);
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
    /// The 022-memory-directory-root re-anchoring (FR-002) touches only
    /// TasksDir/ConversationsDir/FindingsDir/RemediationTasksDir — every genuine runtime-
    /// only data location (raw intake, operational state, agent runtime, write-locks)
    /// still resolves beneath <see cref="ResolvedGrimoirePaths.DataDir"/>, and the secrets
    /// file resolves independently of every root (FR-019).
    /// </summary>
    [Fact]
    public void DataDirLocations_StayAnchoredBeneathDataDir_SecretsFileStaysIndependent()
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
            Assert.Equal("write-locks", DataRelative(resolved.WriteLocksDir));

            // The agent runtime (instructions + workers) resolves beneath AgentDir, itself
            // beneath DataDir in this fixture's explicit layout.
            Assert.StartsWith(resolved.AgentDir, resolved.Ingest.InstructionsDir, StringComparison.Ordinal);
            Assert.StartsWith(resolved.AgentDir, resolved.Query.InstructionsDir, StringComparison.Ordinal);
            Assert.StartsWith(resolved.AgentDir, resolved.Lint.InstructionsDir, StringComparison.Ordinal);

            // The four agent-output relocations (FR-002) live under MemoryDir, not
            // DataDir or WikiDir.
            Assert.DoesNotContain(resolved.DataDir, resolved.TasksDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.DataDir, resolved.ConversationsDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.DataDir, resolved.FindingsDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.DataDir, resolved.RemediationTasksDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.WikiDir, resolved.FindingsDir, StringComparison.Ordinal);
            Assert.StartsWith(resolved.MemoryDir, resolved.FindingsDir, StringComparison.Ordinal);

            // The secrets file is anchored at the process working directory, independent
            // of DataDir/WikiDir/AgentDir alike (FR-019) — this fixture sets it explicitly
            // under the shared temp root, a sibling of all three roots, not nested in any.
            Assert.DoesNotContain(resolved.DataDir, resolved.SecretsFilePath, StringComparison.Ordinal);
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
