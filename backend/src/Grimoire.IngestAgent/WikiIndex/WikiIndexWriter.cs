namespace Grimoire.IngestAgent.WikiIndex;

using Grimoire.IngestAgent.WikiWrite;

public sealed class WikiIndexWriter
{
    private readonly WikiCatalogClassifier _classifier = new();

    public async Task UpdateAsync(string indexPath, string category, string title, string pagePath, string summary, CancellationToken cancellationToken)
    {
        await UpdateFromActionsAsync(indexPath,
            [new IndexUpdateEntry(WikiPageKind.Source, category, title, pagePath, summary)],
            cancellationToken);
    }

    public async Task UpdateFromActionsAsync(string indexPath, IReadOnlyList<IndexUpdateEntry> entries, CancellationToken cancellationToken)
    {
        var existing = File.Exists(indexPath) ? await File.ReadAllTextAsync(indexPath, cancellationToken) : string.Empty;
        var lines = existing.Split('\n').ToList();

        var sortedEntries = entries
            .OrderBy(x => x.PagePath, StringComparer.Ordinal)
            .ThenBy(x => x.Title, StringComparer.Ordinal)
            .ToList();

        foreach (var indexEntry in sortedEntries)
        {
            var category = !string.IsNullOrWhiteSpace(indexEntry.Category)
                ? indexEntry.Category
                : _classifier.Classify(indexEntry.Kind);

            var categoryHeader = $"## {category}";
            if (!lines.Any(l => string.Equals(l.Trim(), categoryHeader, StringComparison.Ordinal)))
            {
                if (lines.Count > 0 && lines[^1].Length > 0)
                {
                    lines.Add(string.Empty);
                }

                lines.Add(categoryHeader);
            }

            var entryLine = $"- [{indexEntry.Title}]({indexEntry.PagePath.Replace('\\', '/')}): {indexEntry.Summary}";
            var categoryIndex = lines.FindIndex(l => string.Equals(l.Trim(), categoryHeader, StringComparison.Ordinal));
            var nextCategoryIndex = lines.FindIndex(categoryIndex + 1, l => l.TrimStart().StartsWith("## ", StringComparison.Ordinal));
            var sectionEnd = nextCategoryIndex >= 0 ? nextCategoryIndex : lines.Count;

            // Only match within this category's section to avoid moving entries across categories.
            var existingEntryIndex = lines.FindIndex(categoryIndex + 1, sectionEnd - (categoryIndex + 1),
                l => l.StartsWith($"- [{indexEntry.Title}]", StringComparison.OrdinalIgnoreCase));

            if (existingEntryIndex >= 0)
            {
                lines[existingEntryIndex] = entryLine;
            }
            else
            {
                lines.Insert(categoryIndex + 1, entryLine);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(indexPath) ?? ".");

        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.update_index");
        span?.SetTag("entry_count", sortedEntries.Count);

        await File.WriteAllTextAsync(indexPath, string.Join("\n", lines).TrimEnd() + "\n", cancellationToken);
    }
}

public sealed record IndexUpdateEntry(
    WikiPageKind Kind,
    string Category,
    string Title,
    string PagePath,
    string Summary);
