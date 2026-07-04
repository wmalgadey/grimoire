using Grimoire.IngestAgent.Guardrails;
using Grimoire.IngestAgent.WikiWrite;

namespace Grimoire.IntegrationTests;

public class GuardrailContinuationTests
{
    [Fact]
    public async Task Writer_ContinuesApplyingAllowedActions_AfterDeniedAction()
    {
        var root = Path.Combine(Path.GetTempPath(), $"guardrail-continuation-{Guid.NewGuid():N}");
        var pagesDir = Path.Combine(root, "wiki", "pages");
        Directory.CreateDirectory(pagesDir);

        var policy = new GuardrailPolicy(
            Version: "1",
            DenyByDefault: true,
            WriteAllowPrefixes: ["wiki/pages/sources/"],
            ReadAllowPaths: [],
            Rules: []);

        var guarded = new GuardedFileOperations(root, new GuardrailEvaluator(policy));
        var writer = new WikiPageWriter();

        var actions = new List<PlannedWikiAction>
        {
            new("create", WikiPageKind.Source, "Source A", "Sources", "Summary", "sources/source-a.md", "# Source A", [], []),
            new("create", WikiPageKind.Entity, "Entity A", "Entities", "Summary", "entities/entity-a.md", "# Entity A", [], []),
        };

        var applied = await writer.ApplyPlannedWritesAsync(pagesDir, actions, guarded, dryRun: false, CancellationToken.None);

        Assert.Single(applied.Where(x => x.Applied));
        Assert.Single(applied.Where(x => x.Denied));
    }
}
