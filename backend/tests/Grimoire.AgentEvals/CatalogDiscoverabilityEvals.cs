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
                sourceContent: "Create one wiki page about evaluation harnesses and keep the catalog discoverable.",
                runLabel: $"sc008-{i + 1}",
                mutateSkillFile: null,
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
        var fileName = Path.GetFileName(pagePath);
        var relativeSuffix = $"pages/{fileName}";

        return indexContent.Contains(fileName, StringComparison.OrdinalIgnoreCase)
            || indexContent.Contains(relativeSuffix, StringComparison.OrdinalIgnoreCase);
    }
}
