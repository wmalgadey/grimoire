using Grimoire.AgentRuntime.Host;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T026 + T027 (029-shared-foundation-prompt, US1, ADR-053, FR-005/SC-002, SC-008/SC-009).
/// Uses the Ingest profile shape (<see cref="AgentProfileFixtures.Ingest"/>) — the fail-closed
/// sequencing and the "reaches the agent" proof are agent-agnostic (<see cref="AgentHost"/>
/// branches on the document, never on the agent type), so one profile is representative;
/// <see cref="FoundationPromptCompositionTests"/> already proves the composition/model-fake
/// path holds identically for all three.
/// </summary>
public class FoundationPromptFailClosedTests
{
    [Theory]
    [InlineData("absent")]
    [InlineData("unreadable")]
    [InlineData("whitespace-only")]
    public async Task MissingUnreadableOrWhitespaceOnlyFoundationDocument_FailsBeforeAnyWikiWrite_NamingTheFoundationDocument(string variant)
    {
        var root = Path.Combine(Path.GetTempPath(), $"foundation-failclosed-{variant}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var foundationPath = Path.Combine(root, "foundation-prompt.md");
            switch (variant)
            {
                case "absent":
                    // No file written at all.
                    break;
                case "unreadable":
                    // A directory in place of the file: unreadable (mirrors
                    // IngestInstructionLoadFailureTests's technique for the role document).
                    Directory.CreateDirectory(foundationPath);
                    break;
                case "whitespace-only":
                    await File.WriteAllTextAsync(foundationPath, " \n\r\t ");
                    break;
            }

            var systemPromptPath = Path.Combine(root, "system-prompt.md");
            await File.WriteAllTextAsync(systemPromptPath, "# Role\nDo the role's job.\n");
            var defaultUserPromptPath = Path.Combine(root, "default-user-prompt.md");
            await File.WriteAllTextAsync(defaultUserPromptPath, "Integrate the source.");

            var fake = new FakeModelClient([FakeModelClient.FinalTurn("should never run")]);
            var intent = new CapturingIntentHandler(root, fake, AgentProfileFixtures.Ingest.ToolRegistry);

            var host = new AgentHost(AgentProfileFixtures.Ingest);
            var run = new AgentHostRun(
                WikiRoot: root,
                FoundationPromptPath: foundationPath,
                SystemPromptPath: systemPromptPath,
                PolicyPath: Path.Combine(root, "does-not-matter-policy.json"),
                HeartbeatSeconds: 30,
                DefaultUserPromptPath: defaultUserPromptPath);
            using var writer = new StringWriter();
            var runEvents = new RunEventEmitter(writer, "run-fail-closed");

            // Snapshot before the run: everything below belongs to test setup, not to the
            // run under test — the wiki-root-unchanged assertion below is against this.
            var entriesBeforeRun = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .OrderBy(e => e, StringComparer.Ordinal).ToArray();

            var exitCode = await host.RunAsync(run, runEvents, intent, CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(intent.ExecuteAsyncWasCalled);
            Assert.Equal(0, fake.CallCount);
            Assert.Equal("foundation_prompt", intent.FailedDocumentKind);
            Assert.Contains(foundationPath, intent.FailureReason, StringComparison.Ordinal);

            // The wiki root is unchanged: the run added nothing beyond what test setup wrote.
            var entriesAfterRun = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .OrderBy(e => e, StringComparer.Ordinal).ToArray();
            Assert.Equal(entriesBeforeRun, entriesAfterRun);
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
    public async Task FoundationConventionAbsentFromRoleDocument_ReachesTheAgent_HermeticHalfOfSC008SC009()
    {
        // The hermetic half of SC-008/SC-009: a convention stated only in the foundation
        // document is present in what the model actually receives, proving the foundation
        // document's content is not merely loaded but composed into the live system prompt.
        // Whether the agent's *judgment* then follows that convention is the lower-stakes
        // half, left to the user-reported correction loop (Principle II).
        var root = Path.Combine(Path.GetTempPath(), $"foundation-reaches-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            const string foundationOnlyConvention =
                "Every page's frontmatter must include a `reviewed_by` field naming the agent that last touched it.";
            var foundationPath = Path.Combine(root, "foundation-prompt.md");
            await File.WriteAllTextAsync(foundationPath, $"# Foundation\n{foundationOnlyConvention}\n");

            var systemPromptPath = Path.Combine(root, "system-prompt.md");
            await File.WriteAllTextAsync(systemPromptPath, "# Role\nDo the ingest job. Nothing about reviewed_by here.\n");
            var defaultUserPromptPath = Path.Combine(root, "default-user-prompt.md");
            await File.WriteAllTextAsync(defaultUserPromptPath, "Integrate the source.");
            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 1,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "."}],
                  "write": [{"pathPrefix": "."}]
                }
                """);

            var fake = new FakeModelClient([FakeModelClient.FinalTurn("final narrative")]);
            var intent = new CapturingIntentHandler(root, fake, AgentProfileFixtures.Ingest.ToolRegistry);

            var host = new AgentHost(AgentProfileFixtures.Ingest);
            var run = new AgentHostRun(
                WikiRoot: root,
                FoundationPromptPath: foundationPath,
                SystemPromptPath: systemPromptPath,
                PolicyPath: policyPath,
                HeartbeatSeconds: 30,
                DefaultUserPromptPath: defaultUserPromptPath);
            using var writer = new StringWriter();
            var runEvents = new RunEventEmitter(writer, "run-reaches-agent");

            var exitCode = await host.RunAsync(run, runEvents, intent, CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(1, fake.CallCount);
            Assert.DoesNotContain(foundationOnlyConvention, "# Role\nDo the ingest job. Nothing about reviewed_by here.\n", StringComparison.Ordinal);
            Assert.Contains(foundationOnlyConvention, fake.Calls[0].SystemPrompt, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
