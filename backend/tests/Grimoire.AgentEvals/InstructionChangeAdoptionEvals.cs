namespace Grimoire.AgentEvals;

public class InstructionChangeAdoptionEvals
{
    [EvalFact]
    public async Task SC009_InstructionEdit_IsAdoptedWithoutSystemChange_AtLeast90Percent()
    {
        var sampleCount = EvalGate.ResolveSampleCount();
        var runner = new AgentEvalRunner();
        var successes = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            var result = await runner.RunAsync(
                fixtureName: "empty-topic",
                sourceContent: "Create a wiki page on ingest governance and follow every required frontmatter field.",
                runLabel: $"sc009-{i + 1}",
                mutateSkillFile: AddReviewedFieldRequirement,
                cancellationToken: CancellationToken.None);

            var touchedFiles = result.Artifact.PagesTouched
                .Select(rel => Path.Combine(result.SandboxRoot, rel))
                .Where(File.Exists)
                .ToList();

            var pass = string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase)
                && touchedFiles.Count > 0
                && touchedFiles.All(path => File.ReadAllText(path).Contains("reviewed: false", StringComparison.OrdinalIgnoreCase));

            if (pass)
            {
                successes++;
            }
        }

        var rate = sampleCount == 0 ? 0 : (double)successes / sampleCount;
        Assert.True(rate >= 0.90, $"SC-009 threshold not met. Success rate: {rate:P1} ({successes}/{sampleCount}).");
    }

    private static void AddReviewedFieldRequirement(string skillPath)
    {
        var baseline = File.ReadAllText(skillPath);
        if (baseline.Contains("reviewed: false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var appended = baseline.TrimEnd() + "\n\n## Temporary Eval Requirement\n- Every written wiki page MUST include frontmatter field reviewed: false.\n";
        File.WriteAllText(skillPath, appended);
    }
}
