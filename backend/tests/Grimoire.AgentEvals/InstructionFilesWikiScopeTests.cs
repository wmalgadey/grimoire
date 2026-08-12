using Grimoire.EvalRunner.Workspace;

namespace Grimoire.AgentEvals;

/// <summary>
/// SC-008 (022-memory-directory-root, FR-012) — none of the Ingest, Query, or Lint
/// system prompts describe tasks/conversations/findings/remediation-tasks as folders
/// reachable within the wiki tree any more: once these folders anchor at
/// <c>MemoryDir</c> instead of <c>WikiDir</c> (ADR-024), that guidance describes a tree
/// the agent can no longer see. A purely lexical assertion over the real prompt sources —
/// no such content test existed before this feature (the pre-existing
/// <c>QueryInstructionLoadTests</c> asserts only byte-identical load + SHA-256).
/// Structurally backed on the production side by ADR-024 rule M3
/// (<c>NoWikiRelativeHarnessRecordLinkRuleTests</c>), which is narrower (it only catches
/// the wikilink form, not prose mentions).
/// </summary>
[Trait("Tier", "Fast")]
public class InstructionFilesWikiScopeTests
{
    private static readonly string[] ForbiddenSubstrings =
        ["tasks/", "conversations/", "findings/", "remediation-tasks/", "[[tasks/"];

    public static IEnumerable<object[]> InstructionFilePaths()
    {
        var paths = EvalPaths.Discover();
        yield return [paths.SystemPromptPath];
        yield return [paths.QuerySystemPromptPath];
        yield return [paths.LintSystemPromptPath];
    }

    [Theory]
    [MemberData(nameof(InstructionFilePaths))]
    public void SystemPrompt_DoesNotReferenceBookkeepingFoldersAsWikiReachable(string systemPromptPath)
    {
        Assert.True(File.Exists(systemPromptPath), $"Expected a real system prompt at {systemPromptPath}.");
        var content = File.ReadAllText(systemPromptPath);

        foreach (var forbidden in ForbiddenSubstrings)
        {
            Assert.DoesNotContain(forbidden, content, StringComparison.Ordinal);
        }
    }
}
