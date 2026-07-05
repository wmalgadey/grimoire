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
                sourceContent: "Create a concise wiki page about offline embedding refresh jobs.",
                runLabel: $"sc007-{i + 1}",
                mutateSkillFile: null,
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

        return content.Contains("\ntitle:", StringComparison.OrdinalIgnoreCase)
            && content.Contains("\ntags:", StringComparison.OrdinalIgnoreCase)
            && content.Contains("\nconfidence:", StringComparison.OrdinalIgnoreCase);
    }
}
