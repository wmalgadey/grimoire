namespace Grimoire.AgentEvals;

public class CatalogDiscoverabilityEvals
{
    [EvalFact]
    public async Task SC008_TouchedPages_AreDiscoverableFromIndex_AtLeast95Percent()
    {
        var sampleCount = EvalGate.ResolveSampleCount();
        var runner = new AgentEvalRunner();
        var successes = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            var result = await runner.RunAsync(
                fixtureName: "empty-topic",
                sourceContent: "Evaluation harnesses are frameworks that automate running a system (an agent, " +
                    "model, or pipeline) against a fixed set of scenarios and scoring its output against a " +
                    "defined threshold, rather than a human manually eyeballing behavior. They typically sample " +
                    "multiple runs per scenario to account for LLM non-determinism, separate deterministic " +
                    "harness checks from evaluation-style judgment scoring, and gate a definition of done on the " +
                    "aggregate pass rate rather than any single run.",
                runLabel: $"sc008-{i + 1}",
                mutateSystemPrompt: null,
                cancellationToken: CancellationToken.None);

            var pass = string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase)
                && result.PageFiles.Count > 0
                && result.PageFiles.All(path => IsDiscoverable(result.IndexContent, path));

            if (pass)
            {
                successes++;
            }
        }

        var rate = sampleCount == 0 ? 0 : (double)successes / sampleCount;
        Assert.True(rate >= 0.95, $"SC-008 threshold not met. Success rate: {rate:P1} ({successes}/{sampleCount}).");
    }

    private static bool IsDiscoverable(string indexContent, string pagePath)
    {
        // Index entries use the extensionless wiki-link convention from SKILL.md:
        // "- [[pages/<slug>]] — <summary>", not the literal filename.
        var slug = Path.GetFileNameWithoutExtension(pagePath);
        var relativeSuffix = $"pages/{slug}";

        return indexContent.Contains(relativeSuffix, StringComparison.OrdinalIgnoreCase)
            || indexContent.Contains(slug, StringComparison.OrdinalIgnoreCase);
    }
}
