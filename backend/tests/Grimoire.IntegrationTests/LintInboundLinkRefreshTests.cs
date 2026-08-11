using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Instructions;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T031 (013-lint-agent, US2, SC-004/SC-008 groundwork): a scripted <c>frontmatter-only</c>
/// <c>write_file</c> updating <c>inbound_links</c> succeeds through the real
/// <see cref="GuardedToolExecutor"/>/policy/guard stack, loaded from the real
/// <c>data/agents/lint/policy.json</c> (T020) via <see cref="PolicyLoader"/> — exactly
/// <see cref="QueryWriteScopeDenialTests"/>'s "runs against the real policy file" idiom,
/// applied to the in-scope success path instead of a denial. The page's body is
/// byte-identical before and after, and this is the only page modification the run
/// performs.
/// </summary>
public class LintInboundLinkRefreshTests
{
    private const string OriginalPage =
        """
        ---
        title: Sample Page
        type: Concept
        tags:
          - concept/Caching
        confidence: medium
        inbound_links: 0
        ---

        # Sample Page

        Body content that must never change during a metadata-only refresh.
        """;

    [Fact]
    public async Task ScriptedFrontmatterOnlyWrite_RefreshingInboundLinks_Succeeds_BodyByteIdentical_OnlyWriteInTheRun()
    {
        var (executor, wikiRoot) = await BuildExecutorAsync();
        var pagePath = Path.Combine(wikiRoot, "tech", "sample-page.md");
        Directory.CreateDirectory(Path.GetDirectoryName(pagePath)!);
        await File.WriteAllTextAsync(pagePath, OriginalPage);

        try
        {
            var refreshedPage =
                """
                ---
                title: Sample Page
                type: Concept
                tags:
                  - concept/Caching
                confidence: medium
                inbound_links: 3
                ---

                # Sample Page

                Body content that must never change during a metadata-only refresh.
                """;

            var fakeModel = new FakeModelClient([
                FakeModelClient.ReadFileTurn("t1", "tech/sample-page.md"),
                FakeModelClient.WriteFileTurn("t2", "tech/sample-page.md", refreshedPage),
                FakeModelClient.FinalTurn("Refreshed the stale inbound-link count."),
            ]);

            var loop = new AgentLoop(fakeModel, executor, registry: LintToolRegistry.Default);

            var result = await loop.RunAsync(
                "You are a test lint agent.",
                [new ConversationMessage("user", "Perform the wiki health check now.")],
                "run-inbound-link-refresh",
                CancellationToken.None);

            Assert.Equal("Refreshed the stale inbound-link count.", result.Narrative);
            Assert.Empty(executor.Denials);

            var onDiskContent = await File.ReadAllTextAsync(pagePath);
            Assert.Equal(refreshedPage, onDiskContent);
            Assert.Contains("inbound_links: 3", onDiskContent, StringComparison.Ordinal);

            // Body byte-identical before/after (everything after the closing `---`).
            var originalBody = OriginalPage[(OriginalPage.IndexOf("---\n\n", StringComparison.Ordinal) + "---\n\n".Length)..];
            var refreshedBody = refreshedPage[(refreshedPage.IndexOf("---\n\n", StringComparison.Ordinal) + "---\n\n".Length)..];
            Assert.Equal(originalBody, refreshedBody);

            // The only page modification the run performs — exactly one touched path.
            var touchedPath = Assert.Single(executor.TouchedPaths);
            Assert.Equal(pagePath, touchedPath);
            Assert.Empty(executor.CreatedPaths);
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    [Fact]
    public async Task ScriptedFrontmatterOnlyWrite_AddingInboundLinksField_WhenPreviouslyAbsent_Succeeds()
    {
        // data-model.md R6: inbound_links is optional/absent on a page Lint has never
        // reviewed yet — the first refresh adds the field rather than requiring it to
        // pre-exist.
        var (executor, wikiRoot) = await BuildExecutorAsync();
        var pagePath = Path.Combine(wikiRoot, "tech", "never-reviewed.md");
        Directory.CreateDirectory(Path.GetDirectoryName(pagePath)!);
        const string pageWithoutInboundLinks =
            """
            ---
            title: Never Reviewed
            type: Concept
            ---

            # Never Reviewed

            Unchanged body.
            """;
        await File.WriteAllTextAsync(pagePath, pageWithoutInboundLinks);

        try
        {
            const string withInboundLinksAdded =
                """
                ---
                title: Never Reviewed
                type: Concept
                inbound_links: 2
                ---

                # Never Reviewed

                Unchanged body.
                """;

            var fakeModel = new FakeModelClient([
                FakeModelClient.ReadFileTurn("t1", "tech/never-reviewed.md"),
                FakeModelClient.WriteFileTurn("t2", "tech/never-reviewed.md", withInboundLinksAdded),
                FakeModelClient.FinalTurn("Added the previously-missing inbound-link count."),
            ]);

            var loop = new AgentLoop(fakeModel, executor, registry: LintToolRegistry.Default);

            await loop.RunAsync(
                "You are a test lint agent.",
                [new ConversationMessage("user", "Perform the wiki health check now.")],
                "run-inbound-link-add",
                CancellationToken.None);

            Assert.Empty(executor.Denials);
            Assert.Equal(withInboundLinksAdded, await File.ReadAllTextAsync(pagePath));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    // ── shared setup (mirrors QueryWriteScopeDenialTests.BuildExecutorAsync) ───────────

    private static async Task<(GuardedToolExecutor Executor, string WikiRoot)> BuildExecutorAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lint-inbound-link-refresh-{Guid.NewGuid():N}");
        var wikiRoot = Path.Combine(root, "wiki");
        Directory.CreateDirectory(Path.Combine(wikiRoot, "tech"));

        var repoRoot = FindRepositoryRoot();
        var policyPath = Path.Combine(repoRoot, "backend", "src", "Grimoire.LintAgent", "Instructions", "policy.json");
        Assert.True(File.Exists(policyPath), $"Expected repo file not found: {policyPath}");

        var loader = new PolicyLoader(wikiRoot);
        var loadResult = await loader.LoadAsync(policyPath, CancellationToken.None);
        Assert.True(loadResult.IsFirst(out var loadedPolicy));

        var journal = new WriteJournal();
        var writeLocksDir = Path.Combine(root, "write-locks");
        var executor = new GuardedToolExecutor(
            loadedPolicy.Policy,
            journal,
            wikiRoot,
            taskId: "run-inbound-link-refresh",
            registry: LintToolRegistry.Default,
            instrumentation: new LintToolCallInstrumentation(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance),
            writeLocksDir: writeLocksDir);

        return (executor, wikiRoot);
    }

    private static void CleanUp(string wikiRoot)
    {
        var root = Path.GetDirectoryName(wikiRoot)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "src", "Grimoire.LintAgent", "Instructions")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root from " + AppContext.BaseDirectory);
    }
}
