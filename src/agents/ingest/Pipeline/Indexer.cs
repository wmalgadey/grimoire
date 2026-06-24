using System.Diagnostics;
using System.Text;
using Grimoire.Ingest.Services;

namespace Grimoire.Ingest.Pipeline;

public class Indexer
{
    private readonly ILogger<Indexer> _logger;

    public Indexer(ILogger<Indexer> logger)
    {
        _logger = logger;
    }

    public async Task<string> WriteIndexAsync(
        string filePath,
        string sourceDir,
        List<ChunkAnalysis> analyses)
    {
        using var activity = IngestTracing.Source.StartActivity("ingest.pipeline.index");
        activity?.SetTag("file_path", filePath);

        var relativePath = Path.GetRelativePath(sourceDir, filePath);
        var outputRelative = Path.ChangeExtension(relativePath, ".md");
        var outputPath = Path.Combine("wiki", outputRelative);

        activity?.SetTag("output_path", outputPath);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var content = BuildMarkdown(filePath, analyses);
        await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8);

        _logger.LogInformation(
            "ingest.pipeline.index file_path={FilePath} output_path={OutputPath}",
            filePath, outputPath);

        return outputPath;
    }

    private static string BuildMarkdown(string filePath, List<ChunkAnalysis> analyses)
    {
        var allTopics = analyses.SelectMany(a => a.Topics).Distinct().OrderBy(t => t).ToList();
        var allEntities = analyses
            .SelectMany(a => a.Entities)
            .GroupBy(e => e.Name.ToLowerInvariant())
            .Select(g => g.First())
            .OrderBy(e => e.Name)
            .ToList();
        var allKeyClaims = analyses.SelectMany(a => a.KeyClaims).Distinct().ToList();
        var contentType = analyses.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.ContentType))?.ContentType ?? "other";
        var chunkCount = analyses.Count;
        var ingestedAt = DateTimeOffset.UtcNow.ToString("o");
        var title = Path.GetFileNameWithoutExtension(filePath);

        var sb = new StringBuilder();

        // YAML frontmatter
        sb.AppendLine("---");
        sb.AppendLine($"title: \"{EscapeYaml(title)}\"");
        sb.AppendLine($"source: \"{EscapeYaml(filePath)}\"");
        sb.AppendLine($"ingested_at: \"{ingestedAt}\"");
        sb.AppendLine($"content_type: \"{contentType}\"");
        sb.AppendLine($"chunk_count: {chunkCount}");

        if (allTopics.Count > 0)
        {
            sb.AppendLine("topics:");
            foreach (var topic in allTopics)
                sb.AppendLine($"  - \"{EscapeYaml(topic)}\"");
        }

        if (allEntities.Count > 0)
        {
            sb.AppendLine("entities:");
            foreach (var entity in allEntities)
                sb.AppendLine($"  - name: \"{EscapeYaml(entity.Name)}\" type: \"{entity.Type}\"");
        }

        sb.AppendLine("---");
        sb.AppendLine();

        // Body
        sb.AppendLine($"# {title}");
        sb.AppendLine();

        // Summary section (first chunk summary)
        var firstSummary = analyses.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Summary))?.Summary;
        if (!string.IsNullOrWhiteSpace(firstSummary))
        {
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine(firstSummary);
            sb.AppendLine();
        }

        // Key Claims section
        if (allKeyClaims.Count > 0)
        {
            sb.AppendLine("## Key Claims");
            sb.AppendLine();
            foreach (var claim in allKeyClaims)
                sb.AppendLine($"- {claim}");
            sb.AppendLine();
        }

        // Topics section
        if (allTopics.Count > 0)
        {
            sb.AppendLine("## Topics");
            sb.AppendLine();
            foreach (var topic in allTopics)
            {
                sb.AppendLine($"### {topic}");
                sb.AppendLine();
                // Include chunk summaries that mention this topic
                var relatedChunks = analyses
                    .Where(a => a.Topics.Contains(topic, StringComparer.OrdinalIgnoreCase))
                    .Where(a => !string.IsNullOrWhiteSpace(a.Summary))
                    .ToList();
                foreach (var chunk in relatedChunks)
                    sb.AppendLine(chunk.Summary);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string EscapeYaml(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
