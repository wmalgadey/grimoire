using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent.AgentCore.Adapters.Replay;

namespace Grimoire.EvalRunner.Scoring;

/// <summary>
/// LLM-judge scoring for the steering-adoption scenario (Principle V: "reflects the
/// steer" is a judgment call and stays an LLM verdict). At capture time the judge is
/// invoked through the <see cref="IModelClient"/> port and its verdict recorded; at
/// replay time the recorded verdict is consumed verbatim — the judge is never re-invoked
/// (research.md R6). The prompt template participates in the staleness fingerprints.
/// </summary>
public static class JudgeScoring
{
    public const string JudgeSystemPrompt =
        "You are a strict, terse evaluation judge. Follow the requested response format exactly.";

    /// <summary>Template with {0}=steer, {1}=narrative, {2}=touched page sections.</summary>
    public const string JudgePromptTemplate =
        """
        You are evaluating whether an ingest agent's run outcome reflects a requested steer.

        Steer given to the agent for this run:
        "{0}"

        Agent's final summary:
        "{1}"

        Touched wiki pages:
        {2}

        Question: does the run's summary and the touched pages' content demonstrably
        reflect the requested steer — i.e. did the agent apply the requested focus,
        rather than ignoring it and processing the source generically?

        Respond with exactly one line: "VERDICT: PASS" or "VERDICT: FAIL", optionally
        followed by a one-sentence reason on the next line.
        """;

    public static string BuildJudgePrompt(string steer, string narrative, IReadOnlyList<string> pageFiles)
    {
        var touchedPageContents = pageFiles
            .Select(p => $"### {Path.GetFileName(p)}\n\n{File.ReadAllText(p)}")
            .ToList();

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            JudgePromptTemplate,
            steer,
            narrative,
            touchedPageContents.Count == 0 ? "(none)" : string.Join("\n\n", touchedPageContents));
    }

    /// <summary>
    /// Invokes the judge once and returns the recorded verdict ("PASS"/"FAIL") plus the
    /// raw response line as rationale.
    /// </summary>
    public static async Task<JudgeVerdict> JudgeAsync(
        IModelClient judge,
        string steer,
        string narrative,
        IReadOnlyList<string> pageFiles,
        CancellationToken cancellationToken)
    {
        var prompt = BuildJudgePrompt(steer, narrative, pageFiles);
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
}
