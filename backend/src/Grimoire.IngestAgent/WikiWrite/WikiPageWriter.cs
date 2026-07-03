namespace Grimoire.IngestAgent.WikiWrite;

public sealed class WikiPageWriter
{
    public async Task<string> WriteAsync(string wikiDir, string relativePath, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(wikiDir);

        var normalized = relativePath.Replace('\\', '/');
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".md";
        }

        if (normalized.StartsWith("wiki/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[5..];
        }

        var fullPath = Path.GetFullPath(Path.Combine(wikiDir, normalized));
        var wikiRoot = Path.GetFullPath(wikiDir);
        if (!fullPath.StartsWith(wikiRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Wiki write attempted outside wiki root.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? wikiDir);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return fullPath;
    }
}
