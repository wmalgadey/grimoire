using System.Text.RegularExpressions;

namespace Grimoire.Domain.Ingest;

public sealed class UpdateOrCreateDecisionService
{
    private static readonly Regex IndexEntryRegex = new(@"^- \[(?<title>.+?)\]\((?<path>.+?)\)", RegexOptions.Multiline | RegexOptions.Compiled);

    public PageDecision Decide(string inferredTitle, string? indexMarkdown)
    {
        var inferredSlug = Slugify(inferredTitle);

        if (!string.IsNullOrWhiteSpace(indexMarkdown))
        {
            foreach (Match match in IndexEntryRegex.Matches(indexMarkdown))
            {
                var title = match.Groups["title"].Value.Trim();
                var path = match.Groups["path"].Value.Trim();

                var titleMatch = string.Equals(title, inferredTitle.Trim(), StringComparison.OrdinalIgnoreCase);
                var slugMatch = path.Contains($"/{inferredSlug}.md", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith($"{inferredSlug}.md", StringComparison.OrdinalIgnoreCase);

                if (titleMatch || slugMatch)
                {
                    return new PageDecision(
                        PageDecisionAction.Update,
                        path,
                        titleMatch
                            ? "Matched existing page title in index."
                            : "Matched existing page slug in index.",
                        ExtractCategoryForMatch(indexMarkdown, match.Index));
                }
            }
        }

        var slug = Slugify(inferredTitle);
        return new PageDecision(
            PageDecisionAction.Create,
                $"sources/{slug}.md",
            "No semantic title match found in index.",
                "Sources");
    }

    private static string ExtractCategoryForMatch(string markdown, int index)
    {
        var lines = markdown.Split('\n');
        var running = 0;
        string currentCategory = "General";
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                currentCategory = line[3..].Trim();
            }

            if (running >= index)
            {
                return currentCategory;
            }

            running += line.Length + 1;
        }

        return currentCategory;
    }

    private static string Slugify(string text)
    {
        var slug = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "untitled" : slug;
    }
}
