namespace Grimoire.IngestAgent.WikiWrite;

public sealed class WikiSupersessionService
{
    public string ApplySupersededBy(string existingContent, string supersededByPath)
    {
        if (string.IsNullOrWhiteSpace(existingContent))
        {
            return existingContent;
        }

        var lines = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count < 3 || lines[0].Trim() != "---")
        {
            return existingContent;
        }

        var end = lines.FindIndex(1, x => x.Trim() == "---");
        if (end < 0)
        {
            return existingContent;
        }

        var index = lines.FindIndex(1, end - 1, x => x.TrimStart().StartsWith("superseded_by:", StringComparison.Ordinal));
        var value = $"superseded_by: \"{supersededByPath.Replace("\\", "/", StringComparison.Ordinal)}\"";

        if (index >= 0)
        {
            lines[index] = value;
        }
        else
        {
            lines.Insert(end, value);
        }

        return string.Join("\n", lines);
    }
}
