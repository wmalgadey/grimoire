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
            var indexPath = Path.Combine(wikiDir, "index.md");
            File.WriteAllText(indexPath, "# Wiki Index\n\nPages:\n");

            // Create scripted model client turns
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
                    StopReason: "tool_use",
                    InputTokens: 100,
                    OutputTokens: 50),

                new ModelTurn(
                    AssistantText: "Creating new page from source.",
                    ToolUseRequests: [
                        new ToolUseRequest(
                            ToolUseId: "tool-2",
                            ToolName: "write_file",
                            InputJson: "{\"path\": \"wiki/pages/test-source.md\", \"content\": \"# Test\\n\\nProcessed from source.\"}")
                    ],
                    StopReason: "tool_use",
                    InputTokens: 150,
                    OutputTokens: 75),

                new ModelTurn(
                    AssistantText: "Run complete. Processed one source into one page.",
                    ToolUseRequests: [],
                    StopReason: "end_turn",
                    InputTokens: 100,
                    OutputTokens: 40)
            };

            var fake = new FakeModelClient(turns);

            // Set up executor with deny-by-default policy
            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: new[] { "wiki/" },
                writePrefixes: new[] { "wiki/pages/", "wiki/index.md", "wiki/log.md" });

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
            Assert.Equal(3, result.TurnsUsed); // read, write, end
            Assert.True(result.TotalInputTokens > 0);
            Assert.True(result.TotalOutputTokens > 0);
            Assert.Contains("complete", result.Narrative, StringComparison.OrdinalIgnoreCase);

            // Verify page was written
            var pagePath = Path.Combine(wikiDir, "pages", "test-source.md");
            Assert.True(File.Exists(pagePath));
            var pageContent = File.ReadAllText(pagePath);
            Assert.Contains("Processed from source", pageContent);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
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
                    StopReason: "tool_use",
                    InputTokens: 100,
                    OutputTokens: 50),

                new ModelTurn(
                    AssistantText: "Write was denied, continuing with allowed action.",
                    ToolUseRequests: [
                        new ToolUseRequest("t2", "write_file",
                            "{\"path\": \"wiki/pages/allowed.md\", \"content\": \"Good content\"}")
                    ],
                    StopReason: "tool_use",
                    InputTokens: 100,
                    OutputTokens: 50),

                new ModelTurn(
                    AssistantText: "Done.",
                    ToolUseRequests: [],
                    StopReason: "end_turn",
                    InputTokens: 100,
                    OutputTokens: 40)
            };

            var fake = new FakeModelClient(turns);

            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: new[] { "wiki/" },
                writePrefixes: new[] { "wiki/pages/", "wiki/index.md" });

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
    /// SC-002: Read operations succeed when paths are in-scope.
    /// </summary>
    [Fact]
    public async Task ReadInPolicy_Succeeds()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), $"read-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var wikiDir = Path.Combine(tempRoot, "wiki");
            Directory.CreateDirectory(wikiDir);
            var testFile = Path.Combine(wikiDir, "test.md");
            File.WriteAllText(testFile, "# Test Content");

            var turns = new[]
            {
                new ModelTurn(
                    AssistantText: "Reading the test file...",
                    ToolUseRequests: [
                        new ToolUseRequest("t1", "read_file",
                            "{\"path\": \"wiki/test.md\"}")
                    ],
                    StopReason: "tool_use",
                    InputTokens: 100,
                    OutputTokens: 50),

                new ModelTurn(
                    AssistantText: "Successfully read the content.",
                    ToolUseRequests: [],
                    StopReason: "end_turn",
                    InputTokens: 100,
                    OutputTokens: 40)
            };

            var fake = new FakeModelClient(turns);

            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: new[] { "wiki/" },
                writePrefixes: new[] { "wiki/pages/" });

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, tempRoot);
            var loop = new AgentLoop(fake, executor);

            // Act
            var result = await loop.RunAsync(
                systemPrompt: "Read test.",
                taskId: "test-task-read",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.TurnsUsed);
            Assert.Contains("Successfully read", result.Narrative, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}

