namespace Grimoire.IngestAgent.WikiWrite;

public sealed class WikiPageWriter
{
    public async Task<string> WriteAsync(string pagesDir, string relativePath, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(pagesDir);

        var fullPath = ResolvePath(pagesDir, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? pagesDir);

        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.write_wiki_page");
        span?.SetTag("page_path", fullPath);

        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return fullPath;
    }

    /// <summary>
    /// Resolves and validates the full path for a wiki page without writing it.
    /// Useful for reading existing content before overwriting (e.g. for rollback on failure).
    /// </summary>
    public string ResolvePath(string pagesDir, string relativePath)
    {
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

        return fullPath;
    }
}
