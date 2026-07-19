using Grimoire.Domain.Guardrails;
using Grimoire.IngestAgent.Guardrails;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// T013t (US1, FR-009) — given a wiki root and absolute paths touched beneath it, the
/// paths recorded for the task artifact (mirroring Program.cs's
/// <c>Path.GetRelativePath(wikiRoot, touchedPath)</c> computation) are content-root-relative:
/// <c>pages/foo.md</c>, never <c>wiki/pages/foo.md</c> or any repo-relative form. Exercises
/// the real guarded-write path (<see cref="GuardedToolExecutor"/> + <see cref="WriteJournal"/>)
/// against temp directories — no LLM call.
/// </summary>
public class ArtifactRelativePathsTests
{
    [Fact]
    public async Task TouchedPaths_RelativizeAgainstWikiRoot_WithNoWikiPrefix_AndNoRepoRelativeSegments()
    {
        // Deliberately named "wiki" so a bug that re-includes the wiki-root's own leaf
        // segment (or a discovered repo root) would surface as a spurious "wiki/" prefix.
        var wikiRoot = Path.Combine(Path.GetTempPath(), $"grimoire-artifact-relpaths-{Guid.NewGuid():N}", "wiki");
        Directory.CreateDirectory(Path.Combine(wikiRoot, "pages"));

        try
        {
            // pages/b.md pre-exists on disk, so the executor's write to it journals as an
            // update rather than a creation.
            var preExistingPath = Path.Combine(wikiRoot, "pages", "b.md");
            await File.WriteAllTextAsync(preExistingPath, "old content");

            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [],
                writePrefixes: [Path.Combine(wikiRoot, "pages") + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, wikiRoot);

            var createResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                System.Text.Json.JsonSerializer.Serialize(new { path = "pages/a.md", content = "new page" }),
                turn: 1, CancellationToken.None);
            Assert.False(createResult.IsError);

            var updateResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                System.Text.Json.JsonSerializer.Serialize(new { path = "pages/b.md", content = "updated content" }),
                turn: 2, CancellationToken.None);
            Assert.False(updateResult.IsError);

            Assert.Equal(2, journal.TouchedPaths.Count);
            Assert.All(journal.TouchedPaths, p => Assert.True(Path.IsPathRooted(p)));

            // Mirrors Program.cs's PagesTouched/PagesCreated/PagesUpdated/PagesSuperseded
            // computation exactly.
            var touchedRelative = journal.TouchedPaths.Select(p => Path.GetRelativePath(wikiRoot, p)).ToList();
            var createdRelative = journal.CreatedPaths.Select(p => Path.GetRelativePath(wikiRoot, p)).ToList();
            var updatedRelative = journal.UpdatedPaths.Select(p => Path.GetRelativePath(wikiRoot, p)).ToList();
            var supersededRelative = journal.SupersededPaths.Select(p => Path.GetRelativePath(wikiRoot, p)).ToList();

            var expectedCreated = Path.Combine("pages", "a.md");
            var expectedUpdated = Path.Combine("pages", "b.md");

            Assert.Contains(expectedCreated, touchedRelative);
            Assert.Contains(expectedUpdated, touchedRelative);
            Assert.Equal([expectedCreated], createdRelative);
            Assert.Equal([expectedUpdated], updatedRelative);
            Assert.Empty(supersededRelative);

            // No relative path carries a "wiki/" prefix, a repo-relative segment, or a
            // traversal-out-of-root marker.
            Assert.All(touchedRelative, p =>
            {
                Assert.False(Path.IsPathRooted(p));
                Assert.DoesNotContain("wiki" + Path.DirectorySeparatorChar, p, StringComparison.Ordinal);
                Assert.DoesNotContain("..", p, StringComparison.Ordinal);
            });
        }
        finally
        {
            var runRoot = Path.GetDirectoryName(wikiRoot)!;
            if (Directory.Exists(runRoot))
            {
                Directory.Delete(runRoot, recursive: true);
            }
        }
    }
}
