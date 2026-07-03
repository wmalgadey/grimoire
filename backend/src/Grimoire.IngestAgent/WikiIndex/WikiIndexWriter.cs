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
        var existingEntryIndex = lines.FindIndex(l => l.StartsWith($"- [{title}]", StringComparison.OrdinalIgnoreCase));
        if (existingEntryIndex >= 0)
        {
            lines[existingEntryIndex] = entryLine;
        }
        else
        {
            var categoryIndex = lines.FindIndex(l => string.Equals(l.Trim(), categoryHeader, StringComparison.Ordinal));
            lines.Insert(categoryIndex + 1, entryLine);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(indexPath) ?? ".");
        await File.WriteAllTextAsync(indexPath, string.Join("\n", lines).TrimEnd() + "\n", cancellationToken);
    }
}
