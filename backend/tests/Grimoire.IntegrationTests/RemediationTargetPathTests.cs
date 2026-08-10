using Grimoire.Domain.Guardrails;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T048 (022-align-wiki-structure, US2) — regression test for T042's live bug fix. Before
/// T042, the lint prompt documented a remediation proposal's <c>targetPath</c> as
/// <c>pages/&lt;slug&gt;.md</c> — a path that cannot exist since feature 014 retired the
/// <c>pages/</c> wrapper, so a Remediation Execution Mode run's very first action
/// (`read_file` against that `targetPath`, per the "Step 1: Re-verify against current
/// content" instruction) always failed. This test proves that a content-root-relative
/// `targetPath` — the shape the lint prompt now emits, e.g. `tech/&lt;slug&gt;.md` — names a
/// real file and the guarded `read_file` call against it succeeds, returning the article's
/// actual content rather than an error.
/// </summary>
public class RemediationTargetPathTests
{
    [Fact]
    public async Task RemediationExecution_FirstReadFile_AgainstContentRootRelativeTargetPath_Succeeds()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"remediation-target-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var wikiDir = Path.Combine(tempRoot, "wiki");
            var articleDir = Path.Combine(wikiDir, "tech");
            Directory.CreateDirectory(articleDir);

            // Exactly the shape T042 fixed the lint prompt's targetPath guidance and worked
            // JSON example to emit: a content-root-relative article path, no "pages/" prefix.
            const string targetPath = "tech/runtime-paths.md";
            const string articleContent =
                "---\ntype: Technology\ntitle: Runtime Paths\ndescription: Test article.\n" +
                "timestamp: 2026-07-14T00:00:00Z\ntags:\n  - tech/dotnet\nconfidence: medium\n" +
                "confidence_reason: One source.\n---\n\nBody text about runtime paths.\n";
            var articleAbsolutePath = Path.Combine(wikiDir, "tech", "runtime-paths.md");
            await File.WriteAllTextAsync(articleAbsolutePath, articleContent);

            var turns = new[]
            {
                FakeModelClient.ReadFileTurn("t1", $"wiki/{targetPath}"),
                FakeModelClient.FinalTurn(
                    "The proposal's premise still holds.\n\n" +
                    "```remediation-outcome\n{\"outcome\": \"applied\", \"reason\": null}\n```"),
            };

            var fake = new FakeModelClient(turns);
            // Remediation re-verification's first action is a read; no write scope is
            // exercised or needed by this regression test.
            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: [wikiDir + Path.DirectorySeparatorChar],
                writePrefixes: []);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, tempRoot, taskId: "task-remediation-target-path");
            var loop = new AgentLoop(fake, executor);

            var result = await loop.RunAsync(
                systemPrompt: "Remediation Execution Mode test.",
                userPrompt: $"Re-verify and, if still warranted, apply. targetPath: {targetPath}",
                taskId: "task-remediation-target-path",
                sourceRef: "remediation://proposal",
                sourceContent: "n/a",
                cancellationToken: CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(2, result.TurnsUsed);
            Assert.Empty(executor.Denials);

            // The second model call's conversation carries the first read_file's tool
            // result — assert it succeeded (not an error) and returned the article's real
            // content, proving the targetPath resolved to an existing file end to end.
            var secondCall = fake.Calls[1];
            var toolResultBlock = Assert.Single(
                secondCall.Conversation.SelectMany(m => m.ContentBlocks).OfType<ConversationToolResultBlock>());

            Assert.False(toolResultBlock.IsError, $"Expected the read_file against '{targetPath}' to succeed, not error: {toolResultBlock.Content}");
            Assert.Equal("t1", toolResultBlock.ToolUseId);
            Assert.Contains("Body text about runtime paths.", toolResultBlock.Content);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
