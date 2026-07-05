using Grimoire.IngestAgent.AgentCore;
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
            var skillsDir = Path.Combine(instructionsDir, "skills", "wiki-maintenance");
            Directory.CreateDirectory(skillsDir);

            var claudePath = Path.Combine(instructionsDir, "CLAUDE.md");
            var skillPath = Path.Combine(skillsDir, "SKILL.md");
            await File.WriteAllTextAsync(claudePath, "You are ingest-agent.");
            await File.WriteAllTextAsync(skillPath, "version: 1");

            var policyPath = Path.Combine(instructionsDir, "policy.json");
            await File.WriteAllTextAsync(
                policyPath,
                """
                {
                  "version": 1,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "wiki/"}],
                  "write": [{"pathPrefix": "wiki/pages/"}]
                }
                """);

            var instructionLoader = new InstructionSetLoader();
            var firstInstructionsResult = await instructionLoader.LoadAsync(instructionsDir, CancellationToken.None);
            Assert.True(firstInstructionsResult.IsFirst(out var firstInstructions));

            var policyLoader = new PolicyLoader(root);
            var policyResult = await policyLoader.LoadAsync(policyPath, CancellationToken.None);
            Assert.True(policyResult.IsFirst(out var loadedPolicy));

            foreach (var file in firstInstructions!.Files)
            {
                var fileBytes = await File.ReadAllBytesAsync(file.Path);
                var expected = Convert.ToHexStringLower(SHA256.HashData(fileBytes));
                Assert.Equal(expected, file.Sha256);
            }

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
                    InstructionFiles: firstInstructions.Files.Select(f => new InstructionFileRecord(f.Path, f.Sha256)).ToList(),
                    Policy: new PolicyRecord(loadedPolicy!.Identity.Path, loadedPolicy.Identity.Version, loadedPolicy.Identity.Sha256),
                    Model: "fake",
                    Turns: 1,
                    RolledBack: null),
                CancellationToken.None);

            var parsed = await store.ReadAsync(artifactPath, CancellationToken.None);
            Assert.NotNull(parsed.InstructionFiles);
            Assert.NotNull(parsed.Policy);
            Assert.Equal(firstInstructions.Files.Count, parsed.InstructionFiles!.Count);
            Assert.Equal(loadedPolicy!.Identity.Sha256, parsed.Policy!.Sha256);
            Assert.Equal(loadedPolicy.Identity.Version, parsed.Policy.Version);

            var firstSkillHash = firstInstructions.Files.Single(f => f.Path.EndsWith("SKILL.md", StringComparison.Ordinal)).Sha256;

            await File.WriteAllTextAsync(skillPath, "version: 2");
            var secondInstructionsResult = await instructionLoader.LoadAsync(instructionsDir, CancellationToken.None);
            Assert.True(secondInstructionsResult.IsFirst(out var secondInstructions));
            var secondSkillHash = secondInstructions!.Files.Single(f => f.Path.EndsWith("SKILL.md", StringComparison.Ordinal)).Sha256;

            Assert.NotEqual(firstSkillHash, secondSkillHash);
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
