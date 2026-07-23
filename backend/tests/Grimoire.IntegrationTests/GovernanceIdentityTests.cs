using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Instructions;
using Grimoire.IngestAgent.TaskArtifact;
using System.Security.Cryptography;
using System.Text;

namespace Grimoire.IntegrationTests;

public class GovernanceIdentityTests
{
    [Fact]
    public async Task InstructionAndPolicyIdentities_AreRecorded_AndSkillEditChangesHash()
    {
        var root = Path.Combine(Path.GetTempPath(), $"governance-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var instructionsDir = Path.Combine(root, "agents", "ingest");
            Directory.CreateDirectory(instructionsDir);

            var systemPromptPath = Path.Combine(instructionsDir, "system-prompt.md");
            await File.WriteAllTextAsync(systemPromptPath, "You are ingest-agent.\nversion: 1");

            var policyPath = Path.Combine(instructionsDir, "policy.json");
            await File.WriteAllTextAsync(
                policyPath,
                """
                {
                  "version": 1,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "pages/"}],
                  "write": [{"pathPrefix": "pages/"}]
                }
                """);

            var instructionLoader = new SystemPromptLoader();
            var firstInstructionsResult = await instructionLoader.LoadAsync(systemPromptPath, CancellationToken.None);
            Assert.True(firstInstructionsResult.IsFirst(out var firstInstructions));

            var policyLoader = new PolicyLoader(root);
            var policyResult = await policyLoader.LoadAsync(policyPath, CancellationToken.None);
            Assert.True(policyResult.IsFirst(out var loadedPolicy));

            var promptBytes = await File.ReadAllBytesAsync(firstInstructions!.Path);
            var expectedHash = Convert.ToHexStringLower(SHA256.HashData(promptBytes));
            Assert.Equal(expectedHash, firstInstructions.Sha256);

            var artifactPath = Path.Combine(root, "wiki", "tasks", "task-1.md");
            var store = new TaskArtifactStore();
            await store.WriteAsync(
                artifactPath,
                new TaskArtifactDocument(
                    TaskId: "task-1",
                    Type: "ingest",
                    Status: "completed",
                    Agent: "ingest",
                    StartedAt: DateTimeOffset.UtcNow,
                    CompletedAt: DateTimeOffset.UtcNow,
                    SourceRef: "source.md",
                    PagesTouched: [],
                    FailureReason: null,
                    Narrative: "Done",
                    PagesCreated: [],
                    PagesUpdated: [],
                    PagesSuperseded: [],
                    DeniedActions: [],
                    InstructionFiles: [new InstructionFileRecord(firstInstructions.Path, firstInstructions.Sha256)],
                    Policy: new PolicyRecord(loadedPolicy!.Identity.Path, loadedPolicy.Identity.Version, loadedPolicy.Identity.Sha256),
                    Model: "fake",
                    Turns: 1,
                    RolledBack: null),
                CancellationToken.None);

            var parsed = await store.ReadAsync(artifactPath, CancellationToken.None);
            Assert.NotNull(parsed.InstructionFiles);
            Assert.NotNull(parsed.Policy);
            Assert.Single(parsed.InstructionFiles!);
            Assert.Equal(firstInstructions.Sha256, parsed.InstructionFiles![0].Sha256);
            Assert.Equal(loadedPolicy!.Identity.Sha256, parsed.Policy!.Sha256);
            Assert.Equal(loadedPolicy.Identity.Version, parsed.Policy.Version);

            var firstHash = firstInstructions.Sha256;

            await File.WriteAllTextAsync(systemPromptPath, "You are ingest-agent.\nversion: 2");
            var secondInstructionsResult = await instructionLoader.LoadAsync(systemPromptPath, CancellationToken.None);
            Assert.True(secondInstructionsResult.IsFirst(out var secondInstructions));

            Assert.NotEqual(firstHash, secondInstructions!.Sha256);
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
