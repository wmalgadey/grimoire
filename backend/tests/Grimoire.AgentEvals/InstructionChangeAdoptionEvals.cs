namespace Grimoire.AgentEvals;

[Collection("EvalProviderEnvironment")]
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
                sourceContent: "Ingest governance is the set of rules and processes that control how external " +
                    "sources are admitted into a managed knowledge system. It typically defines who or what " +
                    "(a human editor or an autonomous agent) is authorized to write, which sources are trusted " +
                    "versus treated as untrusted data, how conflicting or duplicate information is resolved " +
                    "(update, supersede, or flag a contradiction), and what audit trail is kept for every " +
                    "ingest decision so the reasoning behind a page's current state can be reconstructed later.",
                runLabel: $"sc009-{i + 1}",
                mutateSystemPrompt: AddReviewedFieldRequirement,
                cancellationToken: CancellationToken.None);

            // The "reviewed: false" requirement only applies to wiki pages — SKILL.md
            // exempts index.md and log.md from the frontmatter standard entirely.
            var touchedFiles = result.Artifact.PagesTouched
                .Where(rel => !string.Equals(Path.GetFileName(rel), "index.md", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetFileName(rel), "log.md", StringComparison.OrdinalIgnoreCase))
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
