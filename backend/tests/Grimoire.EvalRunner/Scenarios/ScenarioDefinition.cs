using System.Text;

namespace Grimoire.EvalRunner.Scenarios;

/// <summary>One sample's inputs: the pasted source and (steering only) the user prompt.</summary>
public sealed record SampleSpec(string SourceContent, string? UserPrompt);

/// <summary>
/// One evaluated agent-behavior scenario (data-model.md#ScenarioDefinition). Source
/// contents, fixtures, thresholds, and scorer semantics are copied unchanged from the
/// pre-009 `Grimoire.AgentEvals` eval classes; only the execution vehicle moved.
/// </summary>
public sealed record ScenarioDefinition(
    string Id,
    string FixtureName,
    double Threshold,
    bool RequiresNoOutOfScopeWrites,
    IReadOnlyList<SampleSpec> FixedSamples,
    string? RepeatedSourceContent,
    string? SystemPromptAppendix,
    string ScorerId,
    bool JudgeScored)
{
    /// <summary>
    /// Concrete sample list for a capture run: fixed samples (steering) or the repeated
    /// source content at the requested sample count.
    /// </summary>
    public IReadOnlyList<SampleSpec> ResolveSamples(int requestedCount)
        => FixedSamples.Count > 0
            ? FixedSamples
            : Enumerable.Repeat(new SampleSpec(RepeatedSourceContent!, null), requestedCount).ToList();

    /// <summary>
    /// Stable serialization for the `scenario_definition` staleness fingerprint. The
    /// runtime sample count is deliberately excluded — re-recording with more samples is
    /// a refresh decision, not a definition change (research.md R4).
    /// </summary>
    public string StableSerialization()
    {
        var builder = new StringBuilder();
        builder.Append("id=").Append(Id).Append('\n');
        builder.Append("fixture=").Append(FixtureName).Append('\n');
        builder.Append("threshold=").Append(Threshold.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("no_out_of_scope=").Append(RequiresNoOutOfScopeWrites ? "1" : "0").Append('\n');
        builder.Append("scorer=").Append(ScorerId).Append('\n');
        builder.Append("judge=").Append(JudgeScored ? "1" : "0").Append('\n');
        builder.Append("system_prompt_appendix=").Append(SystemPromptAppendix ?? string.Empty).Append('\n');
        builder.Append("source=").Append(RepeatedSourceContent ?? string.Empty).Append('\n');
        foreach (var sample in FixedSamples)
        {
            builder.Append("sample_source=").Append(sample.SourceContent).Append('\n');
            builder.Append("sample_prompt=").Append(sample.UserPrompt ?? string.Empty).Append('\n');
        }

        return builder.ToString();
    }
}
