namespace Grimoire.IngestAgent.WikiWrite;

using Grimoire.IngestAgent.Guardrails;

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

    public async Task<IReadOnlyList<AppliedWikiAction>> ApplyPlannedWritesAsync(
        string pagesDir,
        IReadOnlyList<PlannedWikiAction> actions,
        GuardedFileOperations guardedFileOperations,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var results = new List<AppliedWikiAction>(actions.Count);

        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.apply_wiki_writes");
        span?.SetTag("planned_count", actions.Count);

        foreach (var action in actions)
        {
            var fullPath = ResolvePath(pagesDir, action.RelativePath);
            var relativePath = Path.GetRelativePath(Path.GetDirectoryName(pagesDir) ?? pagesDir, fullPath).Replace('\\', '/');

            if (dryRun)
            {
                results.Add(new AppliedWikiAction(action.Action, action.Kind, relativePath, action.Category, action.Title, action.Summary, true, false));
                continue;
            }

            var wasAllowed = await guardedFileOperations.WriteAllTextAsync(fullPath, action.Content, cancellationToken);
            results.Add(new AppliedWikiAction(action.Action, action.Kind, relativePath, action.Category, action.Title, action.Summary, wasAllowed, !wasAllowed));
        }

        return results;
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

public sealed record AppliedWikiAction(
    string Action,
    WikiPageKind Kind,
    string RelativePath,
    string Category,
    string Title,
    string Summary,
    bool Applied,
    bool Denied);
