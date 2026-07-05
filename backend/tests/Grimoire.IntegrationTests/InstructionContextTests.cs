using Grimoire.Domain.Guardrails;
using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent.Guardrails;
using Grimoire.IntegrationTests.Fakes;
using System.Security.Cryptography;
using System.Text;

namespace Grimoire.IntegrationTests;

public class InstructionContextTests
{
    [Fact]
    public async Task SystemPrompt_ContainsInstructionFilesVerbatim_AndHashesMatchPromptContent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"instruction-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var instructionsDir = Path.Combine(root, "agents", "ingest");
            var skillOneDir = Path.Combine(instructionsDir, "skills", "wiki-maintenance");
            var skillTwoDir = Path.Combine(instructionsDir, "skills", "taxonomy");
            Directory.CreateDirectory(skillOneDir);
            Directory.CreateDirectory(skillTwoDir);

            var claudeContent = "Rule A\nRule B\n";
            var skillOneContent = "Skill One\nDo X\n";
            var skillTwoContent = "Skill Two\nDo Y\n";

            await File.WriteAllTextAsync(Path.Combine(instructionsDir, "CLAUDE.md"), claudeContent);
            await File.WriteAllTextAsync(Path.Combine(skillOneDir, "SKILL.md"), skillOneContent);
            await File.WriteAllTextAsync(Path.Combine(skillTwoDir, "SKILL.md"), skillTwoContent);

            var loader = new InstructionSetLoader();
            var loadResult = await loader.LoadAsync(instructionsDir, CancellationToken.None);
            Assert.True(loadResult.IsFirst(out var loaded));

            var systemPrompt = loaded!.BuildSystemPrompt();
            Assert.Contains(claudeContent.TrimEnd(), systemPrompt, StringComparison.Ordinal);
            Assert.Contains(skillOneContent.TrimEnd(), systemPrompt, StringComparison.Ordinal);
            Assert.Contains(skillTwoContent.TrimEnd(), systemPrompt, StringComparison.Ordinal);

            var fake = new FakeModelClient([
                new ModelTurn("final narrative", [], ModelStopReason.EndTurn, 1, 1)
            ]);

            var policy = new SafetyPolicy(root, readPrefixes: [], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), root);
            var loop = new AgentLoop(fake, executor);

            _ = await loop.RunAsync(systemPrompt, "task-ctx-1", "source.md", "source", CancellationToken.None);

            Assert.Equal(1, fake.CallCount);
            var sentPrompt = fake.Calls[0].SystemPrompt;

            foreach (var file in loaded.Files)
            {
                Assert.Contains($"<!-- {file.Path} -->", sentPrompt, StringComparison.Ordinal);
                Assert.Contains(file.Content.TrimEnd('\r', '\n'), sentPrompt, StringComparison.Ordinal);
                var extractedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(file.Content)));
                Assert.Equal(file.Sha256, extractedHash);
            }
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
