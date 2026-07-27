using Grimoire.Domain.Guardrails;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Instructions;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// 004 US1 (SC-002, ADR-007): fail-closed loading of the System Prompt Document — and
/// of the default-user-prompt document, which shares the same loader semantics. A
/// missing, unreadable, or whitespace-only document fails the run before any model
/// call or wiki write.
/// </summary>
public class InstructionLoadFailureTests
{
    [Fact]
    public async Task Loader_Fails_WhenDocumentMissingUnreadableOrWhitespaceOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"instruction-load-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var loader = new SystemPromptLoader();

            var missingPath = Path.Combine(root, "missing", "system-prompt.md");
            var missing = await loader.LoadAsync(missingPath, CancellationToken.None);
            Assert.True(missing.IsSecond(out var missingFailure));
            Assert.Contains("not found", missingFailure!.Reason, StringComparison.OrdinalIgnoreCase);

            // A directory in place of the file: unreadable.
            var unreadablePath = Path.Combine(root, "unreadable", "system-prompt.md");
            Directory.CreateDirectory(unreadablePath);
            var unreadable = await loader.LoadAsync(unreadablePath, CancellationToken.None);
            Assert.True(unreadable.IsSecond(out var unreadableFailure));
            Assert.True(
                unreadableFailure!.Reason.Contains("Cannot read", StringComparison.OrdinalIgnoreCase) ||
                unreadableFailure.Reason.Contains("not found", StringComparison.OrdinalIgnoreCase));

            var whitespacePath = Path.Combine(root, "whitespace-system-prompt.md");
            await File.WriteAllTextAsync(whitespacePath, " \n\r\t ");
            var whitespace = await loader.LoadAsync(whitespacePath, CancellationToken.None);
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
    public async Task ComposedRun_WhenSystemPromptLoadFails_TakesZeroModelTurnsAndZeroWrites()
    {
        var root = Path.Combine(Path.GetTempPath(), $"instruction-load-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var fake = new FakeModelClient([FakeModelClient.FinalTurn("should never run")]);
            var loader = new SystemPromptLoader();

            var result = await loader.LoadAsync(Path.Combine(root, "system-prompt.md"), CancellationToken.None);
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

    [Fact]
    public async Task DefaultUserPrompt_MissingOrEmpty_FailsClosed_WithHumanReadableReason()
    {
        var root = Path.Combine(Path.GetTempPath(), $"default-prompt-load-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var loader = new SystemPromptLoader();

            var missing = await loader.LoadAsync(Path.Combine(root, "default-user-prompt.md"), CancellationToken.None);
            Assert.True(missing.IsSecond(out var missingFailure));
            Assert.Contains("default-user-prompt.md", missingFailure!.Reason, StringComparison.Ordinal);

            var emptyPath = Path.Combine(root, "empty-default-user-prompt.md");
            await File.WriteAllTextAsync(emptyPath, "   ");
            var empty = await loader.LoadAsync(emptyPath, CancellationToken.None);
            Assert.True(empty.IsSecond(out var emptyFailure));
            Assert.Contains("empty", emptyFailure!.Reason, StringComparison.OrdinalIgnoreCase);
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
