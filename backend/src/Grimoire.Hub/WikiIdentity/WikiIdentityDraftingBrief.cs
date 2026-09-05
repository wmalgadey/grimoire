namespace Grimoire.Hub.WikiIdentity;

/// <summary>
/// Builds the drafting brief FR-013a requires: the operator's own description, quoted
/// verbatim, plus the foundation document's required shape (data-model.md §1) and the
/// invocation that hands a drafted result back (data-model.md §6). States nothing of its
/// own about what a wiki should be — the moment it does, that judgment has crossed the
/// Principle V line into instruction-content territory (plan.md's Agentic Boundary) and
/// belongs in an agent instruction file instead, not here.
/// </summary>
public static class WikiIdentityDraftingBrief
{
    public static string Build(string description) => $"""
        # Foundation Document Drafting Brief

        ## The operator's description (verbatim)

        {description}

        ## Required shape

        Draft one markdown document (no frontmatter) stating:
        - What this wiki is for
        - What belongs in it and what does not
        - How pages are organised and named
        - The conventions that hold across every agent's work (folder structure, page types,
          page language, frontmatter standard, tag taxonomy, confidence scoring, `index.md`/
          `log.md` entry conventions, and how source content is treated)

        It states nothing about any one agent's role, write scope, or steps — those stay in
        each agent's own instruction file.

        ## Handing the result back

        Save the drafted document to a file, then run:

            wiki-identity set --from-file <path-to-drafted-document>

        Add --replace if an instance document is already in place and should be overwritten.
        """;
}
