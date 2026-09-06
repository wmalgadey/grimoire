namespace Grimoire.Hub.WikiIdentity;

/// <summary>
/// Builds the drafting brief FR-013a requires: the operator's own description, quoted
/// verbatim, plus the foundation document's required shape (data-model.md §1) and the
/// invocation that hands a drafted result back (data-model.md §6). States nothing of its
/// own about what a wiki should be — the moment it does, that judgment has crossed the
/// Principle V line into instruction-content territory (plan.md's Agentic Boundary) and
/// belongs in an agent instruction file instead, not here.
///
/// The wording itself lives in <c>DraftingBrief.md</c>, embedded into the assembly rather
/// than kept as a C# string literal — the same reasoning as the root-help Figlet font in
/// <see cref="Cli.HubCliHelpProvider"/>: it is product wording, not logic, and a markdown
/// file is easier to review and edit than an escaped string.
/// </summary>
public static class WikiIdentityDraftingBrief
{
    private const string ResourceName = "Grimoire.Hub.WikiIdentity.DraftingBrief.md";
    private const string DescriptionPlaceholder = "{{DESCRIPTION}}";

    private static readonly Lazy<string> Template = new(() =>
    {
        using var stream = typeof(WikiIdentityDraftingBrief).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded drafting brief '{ResourceName}' is missing from the Grimoire.Hub assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string Build(string description) => Template.Value.Replace(DescriptionPlaceholder, description);
}
