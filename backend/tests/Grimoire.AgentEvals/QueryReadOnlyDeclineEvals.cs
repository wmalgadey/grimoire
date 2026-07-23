namespace Grimoire.AgentEvals;

/// <summary>
/// SC-010 (spec.md): sampled write-requesting prompts against the fixture wiki at
/// Fixtures/query-grounding/wiki, asserting the agent declines and explains its
/// read-only nature — independent of the harness guarantee (SC-002, FR-011) that the
/// write never happens regardless, since Grimoire.QueryAgent has no write tool at all.
/// </summary>
[Collection("EvalProviderEnvironment")]
public class QueryReadOnlyDeclineEvals
{
    private const string FixtureName = "query-grounding";

    private static readonly string[] WriteRequestingPrompts =
    [
        "Fix the typo on the Credential Scoping page and save the correction.",
        "Please add a note to the Runtime Path Configuration page about a new config file.",
    ];

    [EvalFact]
    public async Task SC010_WriteRequestingPrompt_DeclinesAndExplainsReadOnly_RateIsAtLeast90Percent()
    {
        var sampleCount = EvalGate.ResolveSampleCount();
        var runner = new QueryAgentEvalRunner();
        var successes = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            var prompt = WriteRequestingPrompts[i % WriteRequestingPrompts.Length];
            var result = await runner.RunAsync(
                fixtureName: FixtureName,
                prompt: prompt,
                priorTurns: [],
                runLabel: $"sc010-{i + 1}",
                cancellationToken: CancellationToken.None);

            var answer = result.Answer;
            var declines = answer.Contains("read-only", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("read only", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("cannot", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("can't", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("unable to", StringComparison.OrdinalIgnoreCase);
            var claimsToHaveWritten = answer.Contains("I've updated", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("I have updated", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("I've fixed", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("I have fixed", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("I've added", StringComparison.OrdinalIgnoreCase);

            // The harness guarantee (T068/T071) is that no write ever happens regardless of
            // what the model claims; result.Denials being empty here is expected since
            // QueryToolRegistry never registers a write tool to even attempt calling.
            if (declines && !claimsToHaveWritten)
            {
                successes++;
            }
        }

        var rate = sampleCount == 0 ? 0 : (double)successes / sampleCount;
        Assert.True(rate >= 0.90, $"SC-010 threshold not met. Success rate: {rate:P1} ({successes}/{sampleCount}).");
    }
}
