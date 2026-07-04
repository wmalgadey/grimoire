using Grimoire.Domain.Ingest;
using Grimoire.IngestAgent.Synthesis;

namespace Grimoire.IngestAgent.WikiWrite;

public sealed class WikiStructurePlanner
{
    private readonly UpdateOrCreateDecisionService _decisionService;
    private readonly WikiFrontmatterBuilder _frontmatterBuilder;

    public WikiStructurePlanner(UpdateOrCreateDecisionService decisionService, WikiFrontmatterBuilder frontmatterBuilder)
    {
        _decisionService = decisionService;
        _frontmatterBuilder = frontmatterBuilder;
    }

    public IReadOnlyList<PlannedWikiAction> BuildPlan(SynthesisResult synthesis, string indexMarkdown)
    {
        var actions = new List<PlannedWikiAction>();

        foreach (var page in synthesis.PlannedPages)
        {
            var normalizedKind = NormalizeKind(page.Kind);
            var decision = _decisionService.Decide(page.Title, indexMarkdown);
            var targetPath = ResolveTargetPath(normalizedKind, page.Title, decision.TargetPagePath);

            var content = normalizedKind == WikiPageKind.Source
                ? page.Content
                : _frontmatterBuilder.Build(
                    normalizedKind,
                    page.Title,
                    page.Content,
                    page.InboundLinks,
                    supersedes: decision.Action == PageDecisionAction.Update ? [decision.TargetPagePath] : []);

            actions.Add(new PlannedWikiAction(
                Action: decision.Action == PageDecisionAction.Update ? "update" : "create",
                Kind: normalizedKind,
                Title: page.Title,
                Category: string.IsNullOrWhiteSpace(page.Category) ? "General" : page.Category,
                Summary: page.Summary,
                RelativePath: targetPath,
                Content: content,
                InboundLinks: page.InboundLinks,
                Supersedes: decision.Action == PageDecisionAction.Update ? [decision.TargetPagePath] : []));
        }

        return actions
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static WikiPageKind NormalizeKind(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "entity" => WikiPageKind.Entity,
            "concept" => WikiPageKind.Concept,
            _ => WikiPageKind.Source,
        };
    }

    private static string ResolveTargetPath(WikiPageKind kind, string title, string suggestedPath)
    {
        if (kind == WikiPageKind.Source)
        {
            return suggestedPath;
        }

        var slug = Slugify(title);
        var prefix = kind == WikiPageKind.Entity ? "entities" : "concepts";
        return $"{prefix}/{slug}.md";
    }

    private static string Slugify(string text)
    {
        var value = text.ToLowerInvariant();
        var buffer = new List<char>(value.Length);
        var previousDash = false;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Add(character);
                previousDash = false;
                continue;
            }

            if (!previousDash)
            {
                buffer.Add('-');
                previousDash = true;
            }
        }

        return new string(buffer.ToArray()).Trim('-');
    }
}

public enum WikiPageKind
{
    Source,
    Entity,
    Concept,
}

public sealed record PlannedWikiAction(
    string Action,
    WikiPageKind Kind,
    string Title,
    string Category,
    string Summary,
    string RelativePath,
    string Content,
    IReadOnlyList<string> InboundLinks,
    IReadOnlyList<string> Supersedes);
