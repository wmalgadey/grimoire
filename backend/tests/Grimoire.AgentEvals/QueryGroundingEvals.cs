namespace Grimoire.AgentEvals;

/// <summary>
/// SC-007/SC-008 (spec.md): sampled Query agent runs against the fixture wiki at
/// Fixtures/query-grounding/wiki, asserting grounded page-referenced answers for
/// covered questions and honest-gap answers for uncovered ones.
/// </summary>
[Collection("EvalProviderEnvironment")]
public class QueryGroundingEvals
{
    private const string FixtureName = "query-grounding";

    [EvalFact]
    public async Task SC007_CoveredQuestion_GroundedAndPageReferenced_RateIsAtLeast90Percent()
    {
        var sampleCount = EvalGate.ResolveSampleCount();
        var runner = new QueryAgentEvalRunner();
        var successes = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            var result = await runner.RunAsync(
                fixtureName: FixtureName,
                prompt: "How does the Hub keep the Anthropic API key out of its own process environment?",
                priorTurns: [],
                runLabel: $"sc007-{i + 1}",
                cancellationToken: CancellationToken.None);

            var answer = result.Answer;
            var mentionsScopingConcept = answer.Contains("child process", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("child-process", StringComparison.OrdinalIgnoreCase);
            var citesSourcePage = answer.Contains("[[credential-scoping]]", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("Credential Scoping", StringComparison.OrdinalIgnoreCase);

            if (mentionsScopingConcept && citesSourcePage)
            {
                successes++;
            }
        }

        var rate = sampleCount == 0 ? 0 : (double)successes / sampleCount;
        Assert.True(rate >= 0.90, $"SC-007 threshold not met. Success rate: {rate:P1} ({successes}/{sampleCount}).");
    }

    [EvalFact]
    public async Task SC008_UncoveredQuestion_HonestGapAnswer_RateIsAtLeast90Percent()
    {
        var sampleCount = EvalGate.ResolveSampleCount();
        var runner = new QueryAgentEvalRunner();
        var successes = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            var result = await runner.RunAsync(
                fixtureName: FixtureName,
                prompt: "What deployment pipeline does Grimoire use to ship changes to production?",
                priorTurns: [],
                runLabel: $"sc008-{i + 1}",
                cancellationToken: CancellationToken.None);

            var answer = result.Answer;
            var acknowledgesGap = answer.Contains("does not cover", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("doesn't cover", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("no material", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("wiki does not", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("wiki doesn't", StringComparison.OrdinalIgnoreCase);

            var fabricatesPipelineDetail = answer.Contains("GitHub Actions", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("Jenkins", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("Kubernetes deploy", StringComparison.OrdinalIgnoreCase);

            if (acknowledgesGap && !fabricatesPipelineDetail)
            {
                successes++;
            }
        }

        var rate = sampleCount == 0 ? 0 : (double)successes / sampleCount;
        Assert.True(rate >= 0.90, $"SC-008 threshold not met. Success rate: {rate:P1} ({successes}/{sampleCount}).");
    }
}
