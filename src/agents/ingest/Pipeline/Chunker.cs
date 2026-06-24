using System.Text.RegularExpressions;

namespace Grimoire.Ingest.Pipeline;

public record Chunk(string Content, int Index, int TotalChunks, string? Heading);

public class Chunker
{
    private const int MaxChunkSize = 3000;

    public List<Chunk> Chunk(string content, string filePath)
    {
        var blocks = SplitIntoBlocks(content);
        var cappedBlocks = blocks.SelectMany(CapBlock).ToList();

        var result = cappedBlocks
            .Select((block, index) => new Chunk(block.Content, index, 0, block.Heading))
            .ToList();

        // Backfill TotalChunks now that we know the count
        var total = result.Count;
        return result
            .Select(c => c with { TotalChunks = total })
            .ToList();
    }

    private static List<(string Content, string? Heading)> SplitIntoBlocks(string content)
    {
        // Try splitting by H1/H2 markdown headings
        var headingPattern = new Regex(@"^(#{1,2} .+)$", RegexOptions.Multiline);
        var matches = headingPattern.Matches(content);

        if (matches.Count > 0)
        {
            return SplitByHeadings(content, matches);
        }

        // Fall back to paragraph split (double newline)
        var paragraphs = Regex.Split(content, @"\r?\n\r?\n")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => (p.Trim(), (string?)null))
            .ToList();

        return paragraphs.Count > 0
            ? paragraphs
            : new List<(string, string?)> { (content, null) };
    }

    private static List<(string Content, string? Heading)> SplitByHeadings(
        string content,
        MatchCollection matches)
    {
        var blocks = new List<(string Content, string? Heading)>();
        var positions = matches.Cast<Match>().Select(m => m.Index).ToList();

        // Content before the first heading
        if (positions[0] > 0)
        {
            var preamble = content[..positions[0]].Trim();
            if (!string.IsNullOrWhiteSpace(preamble))
                blocks.Add((preamble, null));
        }

        for (var i = 0; i < matches.Count; i++)
        {
            var heading = matches[i].Value;
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? positions[i + 1] : content.Length;
            var sectionContent = content[start..end].Trim();
            var full = string.IsNullOrWhiteSpace(sectionContent)
                ? heading
                : $"{heading}\n\n{sectionContent}";
            blocks.Add((full, heading));
        }

        return blocks;
    }

    private static IEnumerable<(string Content, string? Heading)> CapBlock(
        (string Content, string? Heading) block)
    {
        if (block.Content.Length <= MaxChunkSize)
        {
            yield return block;
            yield break;
        }

        var remaining = block.Content;
        var isFirst = true;

        while (remaining.Length > MaxChunkSize)
        {
            var splitAt = FindSplitPoint(remaining, MaxChunkSize);
            var chunk = remaining[..splitAt].TrimEnd();
            yield return (chunk, isFirst ? block.Heading : null);
            remaining = remaining[splitAt..].TrimStart();
            isFirst = false;
        }

        if (!string.IsNullOrWhiteSpace(remaining))
            yield return (remaining, null);
    }

    private static int FindSplitPoint(string text, int maxLen)
    {
        // Look backwards for a sentence boundary ('. ', '? ', '! ', '\n')
        var end = Math.Min(maxLen, text.Length);

        for (var i = end - 1; i > maxLen / 2; i--)
        {
            if (i < text.Length - 1 &&
                (text[i] == '.' || text[i] == '?' || text[i] == '!') &&
                text[i + 1] == ' ')
            {
                return i + 2;
            }

            if (text[i] == '\n')
                return i + 1;
        }

        // Hard truncate at maxLen
        return end;
    }
}
