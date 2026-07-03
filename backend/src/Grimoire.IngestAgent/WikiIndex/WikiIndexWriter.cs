namespace Grimoire.IngestAgent.WikiIndex;

public sealed class WikiIndexWriter
{
    public async Task UpdateAsync(string indexPath, string category, string title, string pagePath, string summary, CancellationToken cancellationToken)
    {
        var existing = File.Exists(indexPath) ? await File.ReadAllTextAsync(indexPath, cancellationToken) : string.Empty;
        var lines = existing.Split('\n').ToList();

        var categoryHeader = $"## {category}";
        if (!lines.Any(l => string.Equals(l.Trim(), categoryHeader, StringComparison.Ordinal)))
        {
            if (lines.Count > 0 && lines[^1].Length > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add(categoryHeader);
        }

        var entryLine = $"- [{title}]({pagePath.Replace('\\', '/')}): {summary}";
        var categoryIndex = lines.FindIndex(l => string.Equals(l.Trim(), categoryHeader, StringComparison.Ordinal));
        var nextCategoryIndex = lines.FindIndex(categoryIndex + 1, l => l.TrimStart().StartsWith("## ", StringComparison.Ordinal));
        var sectionEnd = nextCategoryIndex >= 0 ? nextCategoryIndex : lines.Count;

        // Only match within this category's section to avoid moving entries across categories.
        var existingEntryIndex = lines.FindIndex(categoryIndex + 1, sectionEnd - (categoryIndex + 1),
            l => l.StartsWith($"- [{title}]", StringComparison.OrdinalIgnoreCase));

        if (existingEntryIndex >= 0)
        {
            lines[existingEntryIndex] = entryLine;
        }
        else
        {
            lines.Insert(categoryIndex + 1, entryLine);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(indexPath) ?? ".");

        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.update_index");
        span?.SetTag("category", category);

        await File.WriteAllTextAsync(indexPath, string.Join("\n", lines).TrimEnd() + "\n", cancellationToken);
    }
}
