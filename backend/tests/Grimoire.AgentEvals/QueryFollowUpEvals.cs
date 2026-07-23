namespace Grimoire.AgentEvals;

/// <summary>
/// SC-009 (spec.md): sampled two-turn conversations against the fixture wiki at
/// Fixtures/query-grounding/wiki where the follow-up question only resolves via a
/// pronoun/reference dependency on the first turn.
/// </summary>
[Collection("EvalProviderEnvironment")]
public class QueryFollowUpEvals
{
    private const string FixtureName = "query-grounding";
    private const string FirstTurnPrompt = "What does the Guarded Write Journal page describe?";

    [EvalFact]
    public async Task SC009_FollowUpReference_ResolvedAgainstPriorTurn_RateIsAtLeast90Percent()
    {
        var sampleCount = EvalGate.ResolveSampleCount();
        var runner = new QueryAgentEvalRunner();
        var successes = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            var firstTurn = await runner.RunAsync(
                fixtureName: FixtureName,
                prompt: FirstTurnPrompt,
                priorTurns: [],
                runLabel: $"sc009-first-{i + 1}",
                cancellationToken: CancellationToken.None);

            var followUp = await runner.RunAsync(
                fixtureName: FixtureName,
                prompt: "Does it use a database transaction for that rollback?",
                priorTurns: [new QueryEvalPriorTurn(FirstTurnPrompt, firstTurn.Answer)],
                runLabel: $"sc009-followup-{i + 1}",
                cancellationToken: CancellationToken.None);

            var answer = followUp.Answer;
            var resolvesReference = answer.Contains("no", StringComparison.OrdinalIgnoreCase)
                && (answer.Contains("transaction", StringComparison.OrdinalIgnoreCase)
                    || answer.Contains("journal", StringComparison.OrdinalIgnoreCase));
            var asksForClarification = answer.Contains("which page", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("what do you mean", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("unclear which", StringComparison.OrdinalIgnoreCase);

            if (resolvesReference && !asksForClarification)
            {
                successes++;
            }
        }

        var rate = sampleCount == 0 ? 0 : (double)successes / sampleCount;
        Assert.True(rate >= 0.90, $"SC-009 threshold not met. Success rate: {rate:P1} ({successes}/{sampleCount}).");
    }
}
