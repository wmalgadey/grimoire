using System.Text.RegularExpressions;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Replay;

namespace Grimoire.EvalRunner.Scoring;

/// <summary>
/// SC-007 (014-wiki-storage-restructure, US4): scores whether a sampled agent-written
/// <c>index.md</c> catalog entry's description specifically and accurately reflects the
/// article it links to, rather than a generic restatement of the article's title
/// (contract: specs/014-wiki-storage-restructure/contracts/log-and-catalog-entry-format.md
/// — "Description: short prose... this is agent-generated wiki content"; ADR-017 only
/// enforces the catalog line's *shape* (link-description-status), never its *content*, so
/// this evaluation-tier scorer is the only judge of description quality, per Constitution
/// Principle V/II).
///
/// LLM-judge scored, mirroring <see cref="LogParagraphSpecificityScorer"/>'s capture/replay
/// split exactly (which itself mirrors <see cref="JudgeScoring"/>): the judge is invoked
/// once against the recorded/live model at capture time and its verdict is replayed
/// verbatim thereafter (research.md R6 precedent) — never re-invoked at replay, so
/// recorded evaluation runs stay fully hermetic.
///
/// Wired (T061) into <see cref="Grimoire.EvalRunner.Scenarios.IngestScenarioDefinitions.CatalogDescriptionSpecificity"/>
/// via <c>IngestCapturePipeline.InvokeCatalogDescriptionJudgeAsync</c>, which judges the most
/// recently added <c>index.md</c> catalog line against the actual content of the article
/// it links to; <c>DeterministicScorers</c>' shared <c>JudgeVerdictGate</c> case reads the
/// recorded verdict at replay (same shape as <see cref="LogParagraphSpecificityScorer"/>).
/// </summary>
public static class CatalogDescriptionSpecificityScorer
{
    // Non-greedy title/path groups (mirrors SharedFileWriteGuard's shape check) plus a
    // single trailing group holding "description — status" together, split separately
    // below on the *last* " — " so an embedded em dash inside the description itself
    // does not misalign the split.
    private static readonly Regex CatalogLinePattern =
        new(@"^- \[(.+?)\]\((.+?)\) — (.+)$", RegexOptions.Compiled);

    public const string JudgeSystemPrompt =
        "You are a strict, terse evaluation judge. Follow the requested response format exactly.";

    /// <summary>Template with {0}=title, {1}=description, {2}=article content.</summary>
    public const string JudgePromptTemplate =
        """
        You are evaluating whether a wiki catalog (index.md) entry's description
        specifically and accurately reflects the article it links to, rather than a
        generic restatement of the article's title or filler text.

        Catalog entry title:
        "{0}"

        Catalog entry description:
        "{1}"

        The article's actual content:
        {2}

        Question: does the description specifically and accurately reflect what the
        article actually covers — naming real facts, topics, or scope drawn from the
        article — rather than a generic restatement of the title (e.g. "Information
        about {0}") or filler that would apply to any article equally?

        Respond with exactly one line: "VERDICT: PASS" or "VERDICT: FAIL", optionally
        followed by a one-sentence reason on the next line.
        """;

    public static string BuildJudgePrompt(string title, string description, string articleContent)
        => string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            JudgePromptTemplate,
            title,
            description,
            string.IsNullOrEmpty(articleContent) ? "(none)" : articleContent);

    /// <summary>
    /// Invokes the judge once and returns the recorded verdict ("PASS"/"FAIL") plus the
    /// raw response line as rationale — same shape as
    /// <see cref="LogParagraphSpecificityScorer.JudgeAsync"/>.
    /// </summary>
    public static async Task<JudgeVerdict> JudgeAsync(
        IModelClient judge,
        string title,
        string description,
        string articleContent,
        CancellationToken cancellationToken)
    {
        var prompt = BuildJudgePrompt(title, description, articleContent);
        var turn = await judge.NextTurnAsync(
            systemPrompt: JudgeSystemPrompt,
            conversation: [new ConversationMessage("user", prompt)],
            tools: [],
            cancellationToken: cancellationToken);

        var text = turn.AssistantText ?? string.Empty;
        var verdict = text.Contains("VERDICT: PASS", StringComparison.Ordinal) ? "PASS" : "FAIL";
        return new JudgeVerdict(
            JudgePromptSha256: RecordingSerialization.Hash(JudgePromptTemplate),
            Verdict: verdict,
            Rationale: text.Length > 500 ? text[..500] : text);
    }

    /// <summary>
    /// Parses one catalog line (<c>"- [Title](path) — description — status"</c>) into
    /// its four parts. Pure string/regex operation; the line is assumed structurally
    /// valid (ADR-017 guarantees this for any newly added line that made it into
    /// <c>index.md</c> at all), so this only extracts text for the judge, never
    /// validates shape. Returns all-empty parts if the line does not match — callers
    /// sampling only lines already known to conform (e.g. from a successful write)
    /// should never observe this.
    /// </summary>
    public static (string Title, string Path, string Description, string StatusMarker) ExtractEntry(string line)
    {
        var match = CatalogLinePattern.Match(line.Trim());
        if (!match.Success)
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }

        var title = match.Groups[1].Value;
        var path = match.Groups[2].Value;
        var rest = match.Groups[3].Value;

        var lastSeparator = rest.LastIndexOf(" — ", StringComparison.Ordinal);
        if (lastSeparator < 0)
        {
            return (title, path, rest, string.Empty);
        }

        var description = rest[..lastSeparator];
        var statusMarker = rest[(lastSeparator + " — ".Length)..];
        return (title, path, description, statusMarker);
    }

    /// <summary>Finds every <c>- [</c>-led catalog line's line index, in file order.</summary>
    public static IReadOnlyList<int> FindCatalogLineIndices(string indexContent)
    {
        var lines = indexContent.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var indices = new List<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("- [", StringComparison.Ordinal))
            {
                indices.Add(i);
            }
        }

        return indices;
    }
}
