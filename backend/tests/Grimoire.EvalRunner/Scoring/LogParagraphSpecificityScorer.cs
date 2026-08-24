using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Replay;

namespace Grimoire.EvalRunner.Scoring;

/// <summary>
/// SC-005 (014-wiki-storage-restructure, US3): scores whether a sampled agent-written
/// <c>log.md</c> paragraph specifically and accurately describes what the run actually
/// did, rather than a generic restatement of its own heading (contract:
/// specs/014-wiki-storage-restructure/contracts/log-and-catalog-entry-format.md — "an
/// entry missing its paragraph... scores as failing the 'specifically and accurately
/// describes what was done' criterion"; ADR-017 only enforces the paragraph's *shape*,
/// never its *content*, so this evaluation-tier scorer is the only judge of quality,
/// per Constitution Principle V/II).
///
/// LLM-judge scored, mirroring <see cref="JudgeScoring"/>'s capture/replay split exactly:
/// the judge is invoked once against the recorded/live model at capture time and its
/// verdict is replayed verbatim thereafter (research.md R6 precedent) — never re-invoked
/// at replay, so recorded evaluation runs stay fully hermetic.
///
/// Wired (T061) into <see cref="Grimoire.EvalRunner.Scenarios.IngestScenarioDefinitions.LogParagraphSpecificity"/>
/// via <c>IngestCapturePipeline.InvokeLogParagraphJudgeAsync</c>, which judges the most
/// recently appended <c>log.md</c> heading/paragraph against the sample's touched pages;
/// <c>DeterministicScorers</c>' shared <c>JudgeVerdictGate</c> case reads the recorded
/// verdict at replay (same shape as <c>SteeringAdoption</c>'s deterministic half).
/// </summary>
public static class LogParagraphSpecificityScorer
{
    public const string JudgeSystemPrompt =
        "You are a strict, terse evaluation judge. Follow the requested response format exactly.";

    /// <summary>Template with {0}=heading, {1}=paragraph, {2}=touched page contents.</summary>
    public const string JudgePromptTemplate =
        """
        You are evaluating whether a wiki-maintenance agent's log.md entry paragraph
        specifically and accurately describes what the run actually did, rather than a
        generic restatement of the heading or vague filler text.

        Log entry heading:
        "{0}"

        Log entry paragraph:
        "{1}"

        What the run actually changed:
        {2}

        Question: does the paragraph specifically and accurately describe the actual
        change shown above — naming the real pages, topics, or sources involved — rather
        than a generic restatement of the heading (e.g. "content was updated") or filler
        that would apply to any run equally?

        Respond with exactly one line: "VERDICT: PASS" or "VERDICT: FAIL", optionally
        followed by a one-sentence reason on the next line.
        """;

    public static string BuildJudgePrompt(string heading, string paragraph, IReadOnlyList<string> touchedPageFiles)
    {
        var touchedPageContents = touchedPageFiles
            .Select(p => $"### {Path.GetFileName(p)}\n\n{File.ReadAllText(p)}")
            .ToList();

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            JudgePromptTemplate,
            heading,
            paragraph,
            touchedPageContents.Count == 0 ? "(none)" : string.Join("\n\n", touchedPageContents));
    }

    /// <summary>
    /// Invokes the judge once and returns the recorded verdict ("PASS"/"FAIL") plus the
    /// raw response line as rationale — same shape as <see cref="JudgeScoring.JudgeAsync"/>.
    /// </summary>
    public static async Task<JudgeVerdict> JudgeAsync(
        IModelClient judge,
        string heading,
        string paragraph,
        IReadOnlyList<string> touchedPageFiles,
        CancellationToken cancellationToken)
    {
        var prompt = BuildJudgePrompt(heading, paragraph, touchedPageFiles);
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
    /// Extracts the heading and paragraph of a <c>log.md</c> entry starting at
    /// <paramref name="headingLineStart"/> (a line beginning with <c>"## ["</c>) — the
    /// entry runs until the next <c>"## "</c> heading or end of file. Pure string
    /// operation; the entry is assumed structurally valid (ADR-017 guarantees this for
    /// any entry that made it into <c>log.md</c> at all), so this only extracts text for
    /// the judge, never validates shape.
    /// </summary>
    public static (string Heading, string Paragraph) ExtractEntry(string logContent, int headingLineStart)
    {
        var lines = logContent.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (headingLineStart < 0 || headingLineStart >= lines.Length)
        {
            return (string.Empty, string.Empty);
        }

        var heading = lines[headingLineStart].Trim();
        var paragraphLines = new List<string>();

        for (var i = headingLineStart + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (paragraphLines.Count > 0)
                {
                    break;
                }

                continue;
            }

            paragraphLines.Add(line.Trim());
        }

        return (heading, string.Join(" ", paragraphLines));
    }

    /// <summary>Finds every <c>## [</c>-led heading's line index, in file order.</summary>
    public static IReadOnlyList<int> FindHeadingLineIndices(string logContent)
    {
        var lines = logContent.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var indices = new List<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("## [", StringComparison.Ordinal))
            {
                indices.Add(i);
            }
        }

        return indices;
    }
}
