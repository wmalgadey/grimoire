namespace Grimoire.IngestAgent.WikiWrite;

public sealed class WikiPageWriter
{
    public async Task<string> WriteAsync(string pagesDir, string relativePath, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(pagesDir);

        var normalized = relativePath.Replace('\\', '/');
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".md";
        }

        if (normalized.StartsWith("pages/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[6..];
        }

        var fullPath = Path.GetFullPath(Path.Combine(pagesDir, normalized));
        var pagesRoot = Path.GetFullPath(pagesDir);
        // Append separator so "/tmp/pages2" cannot match a root of "/tmp/pages"
        if (!pagesRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            pagesRoot += Path.DirectorySeparatorChar;
        }
        if (!fullPath.StartsWith(pagesRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Wiki page write attempted outside pages root.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? pagesDir);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return fullPath;
    }
}
