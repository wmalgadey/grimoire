namespace Grimoire.Hub.IngestSubmission;

/// <summary>
/// One named pre-agent processing step (data-model.md Convert Step): which source kinds
/// it applies to, for which kinds it is required (binary formats cannot skip conversion,
/// FR-013), and its default state.
/// </summary>
public sealed record ConvertStepDefinition(
    string Name,
    IReadOnlySet<string> AppliesTo,
    IReadOnlySet<string> RequiredFor,
    bool DefaultEnabled);

/// <summary>
/// Harness-owned static registry of Convert Steps (FR-011). Exactly one step exists in
/// feature 004 — document-to-Markdown conversion — but the model is a named set so
/// future steps are additive, not a redesign (research R5).
/// </summary>
public static class ConvertStepRegistry
{
    public const string MarkItDown = "markitdown";

    public static readonly IReadOnlyList<ConvertStepDefinition> All =
    [
        new ConvertStepDefinition(
            Name: MarkItDown,
            AppliesTo: new HashSet<string>(StringComparer.Ordinal) { "url", "pdf_file", "office_file" },
            RequiredFor: new HashSet<string>(StringComparer.Ordinal) { "pdf_file", "office_file" },
            DefaultEnabled: true),
    ];

    public static ConvertStepDefinition? TryGet(string name)
        => All.FirstOrDefault(step => string.Equals(step.Name, name, StringComparison.Ordinal));

    /// <summary>Steps applicable to the given source kind label (url, markdown_file, pdf_file, office_file).</summary>
    public static IReadOnlyList<ConvertStepDefinition> StepsFor(string kindLabel)
        => All.Where(step => step.AppliesTo.Contains(kindLabel)).ToList();

    /// <summary>
    /// Resolves the effective per-step configuration for a submission: requested
    /// overrides where present, registry default otherwise. Only applicable steps appear.
    /// </summary>
    public static IReadOnlyDictionary<string, bool> ResolveEffective(
        string kindLabel, IReadOnlyDictionary<string, bool>? requested)
    {
        var effective = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var step in StepsFor(kindLabel))
        {
            effective[step.Name] = requested is not null && requested.TryGetValue(step.Name, out var enabled)
                ? enabled
                : step.DefaultEnabled;
        }

        return effective;
    }
}
