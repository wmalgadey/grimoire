namespace Grimoire.AgentEvals;

public class ConventionAdherenceEvals
{
    [EvalFact]
    public async Task SC007_ProducedPages_FollowInstructionConventions_AtLeast95Percent()
    {
        var sampleCount = EvalGate.ResolveSampleCount();
        var runner = new AgentEvalRunner();
        var successes = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            var result = await runner.RunAsync(
                fixtureName: "empty-topic",
                sourceContent: "Offline embedding refresh jobs are scheduled batch processes that regenerate " +
                    "vector embeddings for a corpus when the underlying embedding model changes or source " +
                    "documents update in bulk. They typically run on a fixed cadence rather than on demand, " +
                    "re-embed the full or incremental document set, and swap the new vectors into the retrieval " +
                    "index atomically to avoid serving mixed-model results mid-transition. Key operational " +
                    "concerns include versioning the embedding model per batch, monitoring drift between old and " +
                    "new vectors, and staging the swap behind a blue/green index so a quality regression can be " +
                    "rolled back.",
                runLabel: $"sc007-{i + 1}",
                mutateSystemPrompt: null,
                cancellationToken: CancellationToken.None);

            var allPagesFollowConvention = result.PageFiles.Count > 0 && result.PageFiles.All(PageHasBasicConventions);
            var pass = string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase)
                && allPagesFollowConvention;

            if (pass)
            {
                successes++;
            }
        }

        var rate = sampleCount == 0 ? 0 : (double)successes / sampleCount;
        Assert.True(rate >= 0.95, $"SC-007 threshold not met. Success rate: {rate:P1} ({successes}/{sampleCount}).");
    }

    private static bool PageHasBasicConventions(string path)
    {
        var content = File.ReadAllText(path);
        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            return false;
        }

        // Required fields per SKILL.md's Frontmatter Standard — note there is no "title" field.
        return content.Contains("\ntags:", StringComparison.OrdinalIgnoreCase)
            && content.Contains("\nconfidence:", StringComparison.OrdinalIgnoreCase)
            && content.Contains("\nconfidence_reason:", StringComparison.OrdinalIgnoreCase)
            && content.Contains("\ninbound_links:", StringComparison.OrdinalIgnoreCase)
            && content.Contains("\nlast_reviewed:", StringComparison.OrdinalIgnoreCase);
    }
}
