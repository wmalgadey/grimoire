using Grimoire.Domain.Guardrails;
using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

public class InstructionLoadFailureTests
{
    [Fact]
    public async Task Loader_Fails_WhenClaudeMissingUnreadableOrWhitespaceOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"instruction-load-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var loader = new InstructionSetLoader();

            var missingDir = Path.Combine(root, "missing");
            Directory.CreateDirectory(missingDir);
            var missing = await loader.LoadAsync(missingDir, CancellationToken.None);
            Assert.True(missing.IsSecond(out var missingFailure));
            Assert.Contains("not found", missingFailure!.Reason, StringComparison.OrdinalIgnoreCase);

            var unreadableDir = Path.Combine(root, "unreadable");
            Directory.CreateDirectory(unreadableDir);
            Directory.CreateDirectory(Path.Combine(unreadableDir, "CLAUDE.md"));
            var unreadable = await loader.LoadAsync(unreadableDir, CancellationToken.None);
            Assert.True(unreadable.IsSecond(out var unreadableFailure));
            Assert.True(
                unreadableFailure!.Reason.Contains("Cannot read", StringComparison.OrdinalIgnoreCase) ||
                unreadableFailure.Reason.Contains("not found", StringComparison.OrdinalIgnoreCase));

            var whitespaceDir = Path.Combine(root, "whitespace");
            Directory.CreateDirectory(whitespaceDir);
            await File.WriteAllTextAsync(Path.Combine(whitespaceDir, "CLAUDE.md"), " \n\r\t ");
            var whitespace = await loader.LoadAsync(whitespaceDir, CancellationToken.None);
            Assert.True(whitespace.IsSecond(out var whitespaceFailure));
            Assert.Contains("whitespace", whitespaceFailure!.Reason, StringComparison.OrdinalIgnoreCase);
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
    public async Task ComposedRun_WhenInstructionLoadFails_TakesZeroModelTurnsAndZeroWrites()
    {
        var root = Path.Combine(Path.GetTempPath(), $"instruction-load-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var fake = new FakeModelClient([FakeModelClient.FinalTurn("should never run")]);
            var loader = new InstructionSetLoader();
            var instructionDir = Path.Combine(root, "instructions");
            Directory.CreateDirectory(instructionDir);

            var result = await loader.LoadAsync(instructionDir, CancellationToken.None);
            Assert.True(result.IsSecond(out _));

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [Path.Combine(root, "wiki", "pages") + Path.DirectorySeparatorChar]);
            var journal = new WriteJournal();
            _ = new GuardedToolExecutor(policy, journal, root);

            Assert.Equal(0, fake.CallCount);
            Assert.Empty(journal.JournaledPaths);
            Assert.False(File.Exists(Path.Combine(root, "wiki", "pages", "result.md")));
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
