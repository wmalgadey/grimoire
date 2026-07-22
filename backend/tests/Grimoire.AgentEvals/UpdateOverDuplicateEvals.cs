namespace Grimoire.AgentEvals;

[Collection("EvalProviderEnvironment")]
public class UpdateOverDuplicateEvals
{
    [EvalFact]
    public async Task SC006_OverlappingSources_UpdateOrSupersedeRate_IsAtLeast90Percent()
    {
        var sampleCount = EvalGate.ResolveSampleCount();
        var runner = new AgentEvalRunner();
        var successes = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            var result = await runner.RunAsync(
                fixtureName: "overlapping-topic",
                sourceContent: "Hybrid retrieval combines sparse lexical search (e.g. BM25) with dense vector " +
                    "search, then merges the two ranked result sets, often via reciprocal rank fusion, to " +
                    "capture both exact term matches and semantic similarity. It consistently outperforms either " +
                    "method alone on queries mixing rare identifiers (error codes, product SKUs) with natural " +
                    "language intent, and has become the default retrieval pattern in production RAG systems.",
                runLabel: $"sc006-{i + 1}",
                mutateSystemPrompt: null,
                cancellationToken: CancellationToken.None);

            var pageCount = result.PageFiles.Count;
            var existingPagePath = result.PageFiles.FirstOrDefault(p => p.EndsWith("retrieval-patterns.md", StringComparison.OrdinalIgnoreCase));
            var existingPageContainsUpdate = existingPagePath is not null
                && File.ReadAllText(existingPagePath).Contains("hybrid retrieval", StringComparison.OrdinalIgnoreCase);

            var pass = string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase)
                && existingPageContainsUpdate
                && pageCount <= 2;

            if (pass)
            {
                successes++;
            }
        }

        var rate = sampleCount == 0 ? 0 : (double)successes / sampleCount;
        Assert.True(rate >= 0.90, $"SC-006 threshold not met. Success rate: {rate:P1} ({successes}/{sampleCount}).");
    }
}
