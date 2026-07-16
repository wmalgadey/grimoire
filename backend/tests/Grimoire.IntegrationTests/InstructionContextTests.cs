using Grimoire.Domain.Guardrails;
using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent.Guardrails;
using Grimoire.IntegrationTests.Fakes;
using System.Security.Cryptography;

namespace Grimoire.IntegrationTests;

/// <summary>
/// 004 US1 (SC-001, ADR-007): the single System Prompt Document is the agent's entire
/// system prompt — byte-exact, no concatenation, no header injection — and the SHA-256
/// recorded per run matches the document on disk.
/// </summary>
public class InstructionContextTests
{
    [Fact]
    public async Task SystemPrompt_IsExactlyTheDocumentContent_AndHashMatchesDisk()
    {
        var root = Path.Combine(Path.GetTempPath(), $"instruction-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var instructionsDir = Path.Combine(root, "agents", "ingest");
            Directory.CreateDirectory(instructionsDir);
            var systemPromptPath = Path.Combine(instructionsDir, "system-prompt.md");

            var promptContent = "# Operating rules\nRule A\nRule B\n\n# Conventions\nDo X\nDo Y\n";
            await File.WriteAllTextAsync(systemPromptPath, promptContent);

            var loader = new SystemPromptLoader();
            var loadResult = await loader.LoadAsync(systemPromptPath, CancellationToken.None);
            Assert.True(loadResult.IsFirst(out var loaded));

            // Verbatim single document — the loaded content IS the system prompt.
            Assert.Equal(promptContent, loaded!.Content);

            var expectedHash = Convert.ToHexStringLower(
                SHA256.HashData(await File.ReadAllBytesAsync(systemPromptPath)));
            Assert.Equal(expectedHash, loaded.Sha256);

            var fake = new FakeModelClient([
                new ModelTurn("final narrative", [], ModelStopReason.EndTurn, 1, 1)
            ]);

            var policy = new SafetyPolicy(root, readPrefixes: [], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), root);
            var loop = new AgentLoop(fake, executor);

            _ = await loop.RunAsync(loaded.Content, "Integrate the source.", "task-ctx-1", "source.md", "source", CancellationToken.None);

            Assert.Equal(1, fake.CallCount);
            // Byte-exact: the model receives exactly the file content, nothing added.
            Assert.Equal(promptContent, fake.Calls[0].SystemPrompt);
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
    public async Task LeftoverLegacyInstructionFiles_AreIgnored_OnlyTheSystemPromptIsLoaded()
    {
        var root = Path.Combine(Path.GetTempPath(), $"instruction-legacy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            // Incomplete-migration scenario (spec edge case): legacy CLAUDE.md and
            // skills/*/SKILL.md still on disk — they must not reach the prompt.
            var instructionsDir = Path.Combine(root, "agents", "ingest");
            var legacySkillDir = Path.Combine(instructionsDir, "skills", "wiki-maintenance");
            Directory.CreateDirectory(legacySkillDir);
            await File.WriteAllTextAsync(Path.Combine(instructionsDir, "CLAUDE.md"), "LEGACY-CLAUDE-CONTENT");
            await File.WriteAllTextAsync(Path.Combine(legacySkillDir, "SKILL.md"), "LEGACY-SKILL-CONTENT");

            var systemPromptPath = Path.Combine(instructionsDir, "system-prompt.md");
            await File.WriteAllTextAsync(systemPromptPath, "Only this document governs the agent.\n");

            var loader = new SystemPromptLoader();
            var loadResult = await loader.LoadAsync(systemPromptPath, CancellationToken.None);
            Assert.True(loadResult.IsFirst(out var loaded));

            Assert.DoesNotContain("LEGACY-CLAUDE-CONTENT", loaded!.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("LEGACY-SKILL-CONTENT", loaded.Content, StringComparison.Ordinal);
            Assert.Equal("Only this document governs the agent.\n", loaded.Content);
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
