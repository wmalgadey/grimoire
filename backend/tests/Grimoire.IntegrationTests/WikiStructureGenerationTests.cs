using Grimoire.IngestAgent.Guardrails;
using Grimoire.IngestAgent.Synthesis;
using Grimoire.IngestAgent.WikiWrite;

namespace Grimoire.IntegrationTests;

public class WikiStructureGenerationTests
{
    [Fact]
    public async Task PlannerAndWriter_Create_SourceEntityConceptPages_InSingleRun()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-structure-{Guid.NewGuid():N}");
        var pagesDir = Path.Combine(root, "wiki", "pages");
        Directory.CreateDirectory(pagesDir);

        var synthesis = new SynthesisResult(
            Title: "Source A",
            Summary: "Root source",
            Category: "Sources",
            Content: "# Source A\n\nSummary",
            PlannedPages:
            [
                new SynthesizedWikiPage("source", "Source A", "Root source", "Sources", "# Source A\n\nSummary", []),
                new SynthesizedWikiPage("entity", "Entity Alpha", "Entity summary", "Entities", "# Entity Alpha\n\nInfo", ["sources/source-a.md"]),
                new SynthesizedWikiPage("concept", "Concept Beta", "Concept summary", "Concepts", "# Concept Beta\n\nInfo", ["sources/source-a.md"]),
            ]);

        var planner = new WikiStructurePlanner(new Grimoire.Domain.Ingest.UpdateOrCreateDecisionService(), new WikiFrontmatterBuilder());
        var actions = planner.BuildPlan(synthesis, string.Empty);

        var evaluator = new GuardrailEvaluator(new GuardrailPolicy(
            "1",
            true,
            ["wiki/"],
            ["specs/"],
            []));
        var guarded = new GuardedFileOperations(root, evaluator);
        var writer = new WikiPageWriter();

        var applied = await writer.ApplyPlannedWritesAsync(pagesDir, actions, guarded, dryRun: false, CancellationToken.None);

        Assert.Equal(3, applied.Count(x => x.Applied));
        Assert.True(File.Exists(Path.Combine(pagesDir, "sources", "source-a.md")));
        Assert.True(File.Exists(Path.Combine(pagesDir, "entities", "entity-alpha.md")));
        Assert.True(File.Exists(Path.Combine(pagesDir, "concepts", "concept-beta.md")));
    }
}
