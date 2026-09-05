using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T029 (029-shared-foundation-prompt, US1) — **Feature-Scoped Invariant** (this feature's
/// resolution surface, contracts/foundation-document.md): with no instance document present,
/// each agent resolves its own build-distributed default; with an instance document present,
/// all three resolve that same file (FR-002, FR-008, SC-007). Exercised directly against the
/// real <c>ResolvedGrimoirePaths.ResolveEffectiveFoundationPrompt</c> — a classicist,
/// state-based behavioral test, never reflection over the record's shape.
/// </summary>
public class FoundationPromptResolutionTests
{
    [Fact]
    public async Task NoInstanceDocument_EachAgentResolvesItsOwnBuildDistributedDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), $"foundation-resolution-default-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);

        try
        {
            await File.WriteAllTextAsync(paths.Ingest.FoundationPromptPath, "ingest's own default foundation");
            await File.WriteAllTextAsync(paths.Query.FoundationPromptPath, "query's own default foundation");
            await File.WriteAllTextAsync(paths.Lint.FoundationPromptPath, "lint's own default foundation");
            Assert.False(File.Exists(paths.InstanceFoundationPromptPath));

            var ingestResolved = paths.ResolveEffectiveFoundationPrompt(paths.Ingest);
            var queryResolved = paths.ResolveEffectiveFoundationPrompt(paths.Query);
            var lintResolved = paths.ResolveEffectiveFoundationPrompt(paths.Lint);

            Assert.Equal("default", ingestResolved.Source);
            Assert.Equal("default", queryResolved.Source);
            Assert.Equal("default", lintResolved.Source);

            Assert.Equal(paths.Ingest.FoundationPromptPath, ingestResolved.Path);
            Assert.Equal(paths.Query.FoundationPromptPath, queryResolved.Path);
            Assert.Equal(paths.Lint.FoundationPromptPath, lintResolved.Path);

            // Each agent's own default content differs, so the recorded hash proves the
            // resolution actually read *that* agent's own file, not a shared one.
            Assert.NotEqual(ingestResolved.Sha256, queryResolved.Sha256);
            Assert.NotEqual(queryResolved.Sha256, lintResolved.Sha256);
            Assert.NotEqual(ingestResolved.Sha256, lintResolved.Sha256);
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
    public async Task InstanceDocumentPresent_AllThreeAgentsResolveThatSameFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"foundation-resolution-instance-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);

        try
        {
            await File.WriteAllTextAsync(paths.InstanceFoundationPromptPath, "the operator's own specialised foundation document");

            var ingestResolved = paths.ResolveEffectiveFoundationPrompt(paths.Ingest);
            var queryResolved = paths.ResolveEffectiveFoundationPrompt(paths.Query);
            var lintResolved = paths.ResolveEffectiveFoundationPrompt(paths.Lint);

            Assert.Equal("instance", ingestResolved.Source);
            Assert.Equal("instance", queryResolved.Source);
            Assert.Equal("instance", lintResolved.Source);

            Assert.Equal(paths.InstanceFoundationPromptPath, ingestResolved.Path);
            Assert.Equal(paths.InstanceFoundationPromptPath, queryResolved.Path);
            Assert.Equal(paths.InstanceFoundationPromptPath, lintResolved.Path);

            // Same file ⇒ same content ⇒ same hash across all three agents.
            Assert.Equal(ingestResolved.Sha256, queryResolved.Sha256);
            Assert.Equal(queryResolved.Sha256, lintResolved.Sha256);
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
