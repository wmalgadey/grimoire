using Grimoire.Domain.Guardrails;
using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T021 - Hermetic agent lifecycle and rollback tests using FakeModelClient.
/// Validates end-to-end agent loop execution, artifact creation, and failure recovery.
/// </summary>
public class AgentRunLifecycleTests
{
    private const string SourceContent = "# Test Source\n\nSome test content for ingest.";

    /// <summary>
    /// SC-001: Multi-write success run completes with correct artifact and pages.
    /// </summary>
    [Fact]
    public async Task SuccessfulAgentRun_WritesPages_AndCompletes()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), $"lifecycle-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            // Create initial wiki structure
            var wikiDir = Path.Combine(tempRoot, "wiki");
            Directory.CreateDirectory(wikiDir);
            var existingPagePath = Path.Combine(wikiDir, "pages", "existing.md");
            Directory.CreateDirectory(Path.GetDirectoryName(existingPagePath)!);
            File.WriteAllText(existingPagePath, "# Existing\n\nOriginal content.");
            var indexPath = Path.Combine(wikiDir, "index.md");
            File.WriteAllText(indexPath, "# Wiki Index\n\nPages:\n");

            // Create scripted model client turns
            const string finalNarrative = "Run complete. Processed one source into one page.";

            var turns = new[]
            {
                new ModelTurn(
                    AssistantText: "Reading wiki structure...",
                    ToolUseRequests: [
                        new ToolUseRequest(
                            ToolUseId: "tool-1",
                            ToolName: "read_file",
                            InputJson: "{\"path\": \"wiki/index.md\"}")
                    ],
                    StopReason: ModelStopReason.ToolUse,
                    InputTokens: 100,
                    OutputTokens: 50),

                new ModelTurn(
                    AssistantText: "Updating the existing page and creating a new one.",
                    ToolUseRequests: [
                        new ToolUseRequest(
                            ToolUseId: "tool-2",
                            ToolName: "write_file",
                            InputJson: "{\"path\": \"wiki/pages/existing.md\", \"content\": \"# Existing\\n\\nUpdated content.\"}"),
                        new ToolUseRequest(
                            ToolUseId: "tool-3",
                            ToolName: "write_file",
                            InputJson: "{\"path\": \"wiki/pages/test-source.md\", \"content\": \"# Test\\n\\nProcessed from source.\"}")
                    ],
                    StopReason: ModelStopReason.ToolUse,
                    InputTokens: 150,
                    OutputTokens: 75),

                new ModelTurn(
                    AssistantText: finalNarrative,
                    ToolUseRequests: [],
                    StopReason: ModelStopReason.EndTurn,
                    InputTokens: 100,
                    OutputTokens: 40)
            };

            var fake = new FakeModelClient(turns);

            // Set up executor with deny-by-default policy
            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: new[] { Path.Combine(tempRoot, "wiki") + Path.DirectorySeparatorChar },
                writePrefixes: new[]
                {
                    Path.Combine(tempRoot, "wiki", "pages") + Path.DirectorySeparatorChar,
                    Path.Combine(tempRoot, "wiki", "index.md"),
                    Path.Combine(tempRoot, "wiki", "log.md"),
                });

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, tempRoot);

            var loop = new AgentLoop(fake, executor);

            // Act
            var result = await loop.RunAsync(
                systemPrompt: "You are a test agent.",
                taskId: "test-task-1",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.TurnsUsed); // read, write turn, end
            Assert.True(result.TotalInputTokens > 0);
            Assert.True(result.TotalOutputTokens > 0);
            Assert.Equal(finalNarrative, result.Narrative);

            // Verify pages were written and journaled with the correct action split.
            Assert.Contains(existingPagePath, journal.UpdatedPaths);
            var pagePath = Path.Combine(wikiDir, "pages", "test-source.md");
            Assert.Contains(pagePath, journal.CreatedPaths);
            Assert.True(File.Exists(pagePath));
            var pageContent = File.ReadAllText(pagePath);
            Assert.Contains("Processed from source", pageContent);

            var updatedContent = File.ReadAllText(existingPagePath);
            Assert.Contains("Updated content", updatedContent);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task MidRunFailureAfterTwoWrites_RollbackRestoresUpdatedAndCreatedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rollback-mid-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var existingPath = Path.Combine(root, "wiki", "pages", "existing.md");
            Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
            await File.WriteAllTextAsync(existingPath, "original");

            var createdPath = Path.Combine(root, "wiki", "pages", "new.md");

            var turns = new[]
            {
                FakeModelClient.WriteFileTurn("w1", "wiki/pages/existing.md", "updated"),
                FakeModelClient.WriteFileTurn("w2", "wiki/pages/new.md", "created"),
                // Unexpected stop reason after writes to force failure.
                new ModelTurn("bad", [], ModelStopReason.Unknown, 1, 1),
            };

            var fake = new FakeModelClient(turns);
            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [Path.Combine(root, "wiki", "pages") + Path.DirectorySeparatorChar]);
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);
            var loop = new AgentLoop(fake, executor);

            await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RunAsync(
                "prompt",
                "task-midrun-failure",
                "source.md",
                "source",
                CancellationToken.None));

            var rollback = await journal.RollbackAsync(CancellationToken.None);
            Assert.All(rollback.Values, Assert.True);

            Assert.Equal("original", await File.ReadAllTextAsync(existingPath));
            Assert.False(File.Exists(createdPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TurnCapBreach_ThrowsAgentLoopCapException_AndRollbackRestoresWiki()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rollback-cap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var existingPath = Path.Combine(root, "wiki", "pages", "existing.md");
            Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
            await File.WriteAllTextAsync(existingPath, "before-cap");

            var turns = new[]
            {
                FakeModelClient.WriteFileTurn("w1", "wiki/pages/existing.md", "during-cap"),
                FakeModelClient.WriteFileTurn("w2", "wiki/pages/new.md", "new"),
            };

            var fake = new FakeModelClient(turns);
            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [Path.Combine(root, "wiki", "pages") + Path.DirectorySeparatorChar]);
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);
            var loop = new AgentLoop(fake, executor, turnCap: 1);

            var ex = await Assert.ThrowsAsync<AgentLoopCapException>(() => loop.RunAsync(
                "prompt",
                "task-cap",
                "source.md",
                "source",
                CancellationToken.None));

            Assert.Equal("turns", ex.Cap);

            var rollback = await journal.RollbackAsync(CancellationToken.None);
            Assert.All(rollback.Values, Assert.True);
            Assert.Equal("before-cap", await File.ReadAllTextAsync(existingPath));
            Assert.False(File.Exists(Path.Combine(root, "wiki", "pages", "new.md")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Policy denial: write outside policy scope is denied and logged.
    /// </summary>
    [Fact]
    public async Task OutOfPolicyWrite_Denied_AndContinues()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), $"denial-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var wikiDir = Path.Combine(tempRoot, "wiki");
            Directory.CreateDirectory(wikiDir);

            // Attempt to write outside policy scope, then a valid write
            var turns = new[]
            {
                new ModelTurn(
                    AssistantText: "Attempting unauthorized write...",
                    ToolUseRequests: [
                        new ToolUseRequest("t1", "write_file",
                            "{\"path\": \"forbidden/file.md\", \"content\": \"Bad content\"}")
                    ],
                    StopReason: ModelStopReason.ToolUse,
                    InputTokens: 100,
                    OutputTokens: 50),

                new ModelTurn(
                    AssistantText: "Write was denied, continuing with allowed action.",
                    ToolUseRequests: [
                        new ToolUseRequest("t2", "write_file",
                            "{\"path\": \"wiki/pages/allowed.md\", \"content\": \"Good content\"}")
                    ],
                    StopReason: ModelStopReason.ToolUse,
                    InputTokens: 100,
                    OutputTokens: 50),

                new ModelTurn(
                    AssistantText: "Done.",
                    ToolUseRequests: [],
                    StopReason: ModelStopReason.EndTurn,
                    InputTokens: 100,
                    OutputTokens: 40)
            };

            var fake = new FakeModelClient(turns);

            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: new[] { Path.Combine(tempRoot, "wiki") + Path.DirectorySeparatorChar },
                writePrefixes: new[]
                {
                    Path.Combine(tempRoot, "wiki", "pages") + Path.DirectorySeparatorChar,
                    Path.Combine(tempRoot, "wiki", "index.md"),
                });

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, tempRoot);
            var loop = new AgentLoop(fake, executor);

            // Act
            var result = await loop.RunAsync(
                systemPrompt: "Test.",
                taskId: "test-task-3",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.TurnsUsed);

            // Forbidden file should not exist
            var forbiddenFile = Path.Combine(tempRoot, "forbidden", "file.md");
            Assert.False(File.Exists(forbiddenFile));
            Assert.Single(executor.Denials);
            Assert.Equal("forbidden/file.md", executor.Denials[0].RequestedTarget);
            Assert.Equal(forbiddenFile, executor.Denials[0].CanonicalTarget);

            // Allowed file should exist
            var allowedFile = Path.Combine(wikiDir, "pages", "allowed.md");
            Assert.True(File.Exists(allowedFile));
            Assert.Contains("Good content", File.ReadAllText(allowedFile));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// Minimal test: Single write to repo root, verify file exists and content matches.
    /// </summary>
    [Fact]
    public async Task MinimalWrite_ToRepoRoot_Succeeds()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), $"minimal-write-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            // Single turn: write to root
            var turns = new[]
            {
                new ModelTurn(
                    AssistantText: "Writing test output file.",
                    ToolUseRequests: [
                        new ToolUseRequest(
                            ToolUseId: "t1",
                            ToolName: "write_file",
                            InputJson: "{\"path\": \"output.txt\", \"content\": \"Test content from agent.\"}")
                    ],
                    StopReason: ModelStopReason.ToolUse,
                    InputTokens: 100,
                    OutputTokens: 50),

                new ModelTurn(
                    AssistantText: "Done.",
                    ToolUseRequests: [],
                    StopReason: ModelStopReason.EndTurn,
                    InputTokens: 100,
                    OutputTokens: 40)
            };

            var fake = new FakeModelClient(turns);

            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: Array.Empty<string>(),
                writePrefixes: new[] { tempRoot + Path.DirectorySeparatorChar }); // Allow all writes under the temp repo root

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, tempRoot);
            var loop = new AgentLoop(fake, executor);

            // Act
            var result = await loop.RunAsync(
                systemPrompt: "You are a test agent.",
                taskId: "minimal-test",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.TurnsUsed);

            // Check file exists
            var outputFile = Path.Combine(tempRoot, "output.txt");
            Assert.True(File.Exists(outputFile), $"File not found at {outputFile}");
            Assert.Equal("Test content from agent.", File.ReadAllText(outputFile));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task NonTerminalTurnWithoutTools_DoesNotEmitEmptyUserMessage()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), $"no-empty-user-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var turns = new[]
            {
                new ModelTurn(
                    AssistantText: "I need one more turn.",
                    ToolUseRequests: [],
                    StopReason: ModelStopReason.MaxTokens,
                    InputTokens: 120,
                    OutputTokens: 60),

                new ModelTurn(
                    AssistantText: "Done.",
                    ToolUseRequests: [],
                    StopReason: ModelStopReason.EndTurn,
                    InputTokens: 80,
                    OutputTokens: 40),
            };

            var fake = new FakeModelClient(turns);
            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: Array.Empty<string>(),
                writePrefixes: Array.Empty<string>());
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, tempRoot);
            var loop = new AgentLoop(fake, executor);

            // Act
            var result = await loop.RunAsync(
                systemPrompt: "Test.",
                taskId: "test-task-no-empty-user",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None);

            // Assert
            Assert.Equal(2, result.TurnsUsed);
            Assert.Equal(2, fake.CallCount);

            foreach (var call in fake.Calls)
            {
                foreach (var message in call.Conversation)
                {
                    if (string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.False(string.IsNullOrWhiteSpace(message.Content));
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void RawStopReasonParser_NormalizesPascalCaseAndSnakeCaseValues()
    {
        Assert.Equal(ModelStopReason.EndTurn, ModelStopReasonContract.FromRawValue("EndTurn"));
        Assert.Equal(ModelStopReason.EndTurn, ModelStopReasonContract.FromRawValue("end_turn"));
        Assert.Equal(ModelStopReason.ToolUse, ModelStopReasonContract.FromRawValue("ToolUse"));
    }

    [Fact]
    public async Task StopSequenceWithoutTools_ThrowsForUnexpectedStopReason()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), $"stop-sequence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var turns = new[]
            {
                new ModelTurn(
                    AssistantText: "Final response from stop sequence.",
                    ToolUseRequests: [],
                    StopReason: ModelStopReason.StopSequence,
                    InputTokens: 30,
                    OutputTokens: 20),
            };

            var fake = new FakeModelClient(turns);
            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: Array.Empty<string>(),
                writePrefixes: Array.Empty<string>());
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, tempRoot);
            var loop = new AgentLoop(fake, executor);

            // Act / Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RunAsync(
                systemPrompt: "Test.",
                taskId: "test-task-stop-sequence",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None));

            Assert.Contains("unexpected stop_reason", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("stop_sequence", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ToolUseStopReasonWithoutToolBlocks_Throws()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), $"tool-use-without-blocks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var turns = new[]
            {
                new ModelTurn(
                    AssistantText: "Inconsistent stop reason.",
                    ToolUseRequests: [],
                    StopReason: ModelStopReason.ToolUse,
                    InputTokens: 40,
                    OutputTokens: 25),
            };

            var fake = new FakeModelClient(turns);
            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: Array.Empty<string>(),
                writePrefixes: Array.Empty<string>());
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, tempRoot);
            var loop = new AgentLoop(fake, executor);

            // Act / Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RunAsync(
                systemPrompt: "Test.",
                taskId: "test-task-tool-use-no-blocks",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None));

            Assert.Contains("stop_reason=tool_use", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task EmptyStopReasonWithoutTools_ThrowsForMissingRuntimeStopCondition()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), $"empty-stop-reason-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var turns = new[]
            {
                new ModelTurn(
                    AssistantText: "No explicit stop condition.",
                    ToolUseRequests: [],
                    StopReason: ModelStopReason.Unknown,
                    InputTokens: 11,
                    OutputTokens: 7),
            };

            var fake = new FakeModelClient(turns);
            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: Array.Empty<string>(),
                writePrefixes: Array.Empty<string>());
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, tempRoot);
            var loop = new AgentLoop(fake, executor);

            // Act / Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RunAsync(
                systemPrompt: "Test.",
                taskId: "test-task-empty-stop-reason",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None));

            Assert.Contains("unexpected stop_reason", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("unknown", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

}
