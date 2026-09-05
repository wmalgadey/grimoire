namespace Grimoire.Hub.LintFindings;

/// <summary>
/// Mechanical counting over the agent's own narrative structure (contracts/
/// findings-report-format.md: three fixed <c>## &lt;Category&gt;</c> headings, each with
/// zero or more <c>### &lt;finding title&gt;</c> sections) — never a judgment about
/// whether a heading represents a genuine problem (Constitution Principle V: the harness
/// counts headings the agent already wrote, it does not decide what counts as a finding).
/// Used for the <c>lint.run.completed</c> log event's mandatory <c>findings_count</c>
/// field (plan.md ## Observability) and, from T037 onward, the
/// <c>wiki.lint.findings_total{category}</c> metric's per-category breakdown.
/// </summary>
public static class LintFindingsNarrativeStats
{
    private const string ContentQualityHeading = "## Content Quality";
    private const string MetadataHygieneHeading = "## Metadata Hygiene";
    private const string StructureHeading = "## Structure";

    /// <summary>Total number of <c>### </c> finding sections anywhere in the narrative.</summary>
    public static int CountFindings(string narrative) => CountByCategory(narrative).Values.Sum();

    /// <summary>
    /// Finding count per known category heading (<c>content_quality</c>/
    /// <c>metadata_hygiene</c>/<c>structure</c>) — a <c>### </c> line outside any known
    /// category heading (malformed narrative) is not counted under any key rather than
    /// silently attributed to the wrong category.
    /// </summary>
    public static IReadOnlyDictionary<string, int> CountByCategory(string narrative)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["content_quality"] = 0,
            ["metadata_hygiene"] = 0,
            ["structure"] = 0,
        };

        string? currentCategory = null;
        foreach (var rawLine in narrative.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith(ContentQualityHeading, StringComparison.Ordinal))
            {
                currentCategory = "content_quality";
            }
            else if (line.StartsWith(MetadataHygieneHeading, StringComparison.Ordinal))
            {
                currentCategory = "metadata_hygiene";
            }
            else if (line.StartsWith(StructureHeading, StringComparison.Ordinal))
            {
                currentCategory = "structure";
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                // Any other top-level heading (e.g. the leading "# Lint Run ..." title
                // does not match "## " at all) closes whichever known category was open.
                currentCategory = null;
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal) && currentCategory is not null)
            {
                counts[currentCategory]++;
            }
        }

        return counts;
    }
}
