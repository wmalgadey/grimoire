namespace Grimoire.IngestAgent.WikiWrite;

public sealed class WikiFrontmatterBuilder
{
    public string Build(
        WikiPageKind kind,
        string title,
        string content,
        IReadOnlyList<string> inboundLinks,
        IReadOnlyList<string>? supersedes = null,
        string? supersededBy = null)
    {
        var tags = kind == WikiPageKind.Entity
            ? new[] { "entity", "ingest" }
            : new[] { "concept", "ingest" };

        var links = inboundLinks.Count == 0
            ? "[]"
            : $"[{string.Join(", ", inboundLinks.Select(x => $"\"{Escape(x)}\""))}]";

        var supersedesYaml = (supersedes is { Count: > 0 })
            ? $"supersedes: [{string.Join(", ", supersedes.Select(x => $"\"{Escape(x)}\""))}]\n"
            : string.Empty;

        var supersededByYaml = string.IsNullOrWhiteSpace(supersededBy)
            ? string.Empty
            : $"superseded_by: \"{Escape(supersededBy)}\"\n";

        return "---\n" +
               $"title: \"{Escape(title)}\"\n" +
               $"kind: {kind.ToString().ToLowerInvariant()}\n" +
               $"tags: [{string.Join(", ", tags.Select(x => $"\"{x}\""))}]\n" +
               "confidence: 0.6\n" +
               "confidence_reason: \"Inferred from source synthesis and existing wiki context.\"\n" +
               $"inbound_links: {links}\n" +
               $"last_reviewed: {DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}\n" +
               supersedesYaml +
               supersededByYaml +
               "---\n\n" +
               content.Trim() + "\n";
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
