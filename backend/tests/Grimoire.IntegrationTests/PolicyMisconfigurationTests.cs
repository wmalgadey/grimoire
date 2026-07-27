using Grimoire.Domain.Guardrails;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Instructions;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

public class PolicyMisconfigurationTests
{
    [Fact]
    public async Task PolicyLoader_FailsClosed_ForMissingUnparseableUnknownPropsAndBadDefaultDecision()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy-misconfig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var loader = new PolicyLoader(root);

            var missingPath = Path.Combine(root, "missing-policy.json");
            var missing = await loader.LoadAsync(missingPath, CancellationToken.None);
            Assert.True(missing.IsSecond(out _));

            var unparseablePath = Path.Combine(root, "unparseable.json");
            await File.WriteAllTextAsync(unparseablePath, "{");
            var unparseable = await loader.LoadAsync(unparseablePath, CancellationToken.None);
            Assert.True(unparseable.IsSecond(out _));

            var unknownPropsPath = Path.Combine(root, "unknown.json");
            await File.WriteAllTextAsync(
                unknownPropsPath,
                """
                {
                  "version": 1,
                  "defaultDecision": "deny",
                  "read": [],
                  "write": [],
                  "extra": true
                }
                """);
            var unknownProps = await loader.LoadAsync(unknownPropsPath, CancellationToken.None);
            Assert.True(unknownProps.IsSecond(out _));

            var badDefaultPath = Path.Combine(root, "bad-default.json");
            await File.WriteAllTextAsync(
                badDefaultPath,
                """
                {
                  "version": 1,
                  "defaultDecision": "allow",
                  "read": [],
                  "write": []
                }
                """);
            var badDefault = await loader.LoadAsync(badDefaultPath, CancellationToken.None);
            Assert.True(badDefault.IsSecond(out _));
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
    public async Task AllDenyWritePolicy_RecordsEveryIntendedWriteAsDenial_AndNoFilesAreWritten()
    {
        var root = Path.Combine(Path.GetTempPath(), $"all-deny-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: []);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);
            var fake = new FakeModelClient(
            [
                FakeModelClient.WriteFileTurn("w1", "wiki/pages/a.md", "A"),
                FakeModelClient.WriteFileTurn("w2", "wiki/pages/b.md", "B"),
                FakeModelClient.FinalTurn("done")
            ]);

            var loop = new AgentLoop(fake, executor);
            var result = await loop.RunAsync("prompt", "Integrate the source.", "task-policy-1", "source.md", "src", CancellationToken.None);

            Assert.Equal(3, result.TurnsUsed);
            Assert.Equal(2, executor.Denials.Count);
            Assert.Empty(journal.TouchedPaths);
            Assert.False(File.Exists(Path.Combine(root, "wiki", "pages", "a.md")));
            Assert.False(File.Exists(Path.Combine(root, "wiki", "pages", "b.md")));

            var runShouldFail = journal.TouchedPaths.Count == 0 && executor.Denials.Count > 0;
            Assert.True(runShouldFail);
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
