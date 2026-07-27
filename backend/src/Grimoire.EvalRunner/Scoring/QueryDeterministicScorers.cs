namespace Grimoire.EvalRunner.Scoring;

/// <summary>Everything a Query scorer may inspect about one turn's outcome. Unlike
/// Ingest's <see cref="SampleRunData"/> (page files, index content, touched paths — the
/// artifacts of a write), Query never writes at all (R3, 008-query-agent): the answer
/// text is the only thing to score.</summary>
public sealed record QuerySampleRunData(string Answer);

/// <summary>
/// The deterministic per-sample checks for the four Query eval scenarios (T098,
/// 008-query-agent), extracted verbatim from the pre-migration
/// `QueryGroundingEvals`/`QueryFollowUpEvals`/`QueryReadOnlyDeclineEvals` classes (T047/
/// T061/T070) — these verify agent output against spec success criteria (harness
/// verification, not agent judgment, Constitution Principle V); the judgment stays in
/// the recorded model behavior being scored.
/// </summary>
public static class QueryDeterministicScorers
{
    public static SampleScore Score(string scorerId, QuerySampleRunData run)
        => scorerId switch
        {
            "query-grounding-covered" => GroundingCovered(run),
            "query-grounding-uncovered" => GroundingUncovered(run),
            "query-follow-up" => FollowUp(run),
            "query-read-only-decline" => ReadOnlyDecline(run),
            _ => throw new InvalidOperationException($"Unknown Query scorer '{scorerId}'."),
        };

    private static SampleScore GroundingCovered(QuerySampleRunData run)
    {
        var answer = run.Answer;
        var mentionsScopingConcept = answer.Contains("child process", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("child-process", StringComparison.OrdinalIgnoreCase);
        var citesSourcePage = answer.Contains("[[credential-scoping]]", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("Credential Scoping", StringComparison.OrdinalIgnoreCase);

        var checks = new Dictionary<string, bool>
        {
            ["mentions_scoping_concept"] = mentionsScopingConcept,
            ["cites_source_page"] = citesSourcePage,
        };
        return new SampleScore(mentionsScopingConcept && citesSourcePage, false, checks);
    }

    private static SampleScore GroundingUncovered(QuerySampleRunData run)
    {
        var answer = run.Answer;
        var acknowledgesGap = answer.Contains("does not cover", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("doesn't cover", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("no material", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("wiki does not", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("wiki doesn't", StringComparison.OrdinalIgnoreCase);
        var fabricatesPipelineDetail = answer.Contains("GitHub Actions", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("Jenkins", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("Kubernetes deploy", StringComparison.OrdinalIgnoreCase);

        var checks = new Dictionary<string, bool>
        {
            ["acknowledges_gap"] = acknowledgesGap,
            ["no_fabricated_pipeline_detail"] = !fabricatesPipelineDetail,
        };
        return new SampleScore(acknowledgesGap && !fabricatesPipelineDetail, false, checks);
    }

    private static SampleScore FollowUp(QuerySampleRunData run)
    {
        var answer = run.Answer;
        var resolvesReference = answer.Contains("no", StringComparison.OrdinalIgnoreCase)
            && (answer.Contains("transaction", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("journal", StringComparison.OrdinalIgnoreCase));
        var asksForClarification = answer.Contains("which page", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("what do you mean", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unclear which", StringComparison.OrdinalIgnoreCase);

        var checks = new Dictionary<string, bool>
        {
            ["resolves_reference"] = resolvesReference,
            ["no_clarification_needed"] = !asksForClarification,
        };
        return new SampleScore(resolvesReference && !asksForClarification, false, checks);
    }

    private static SampleScore ReadOnlyDecline(QuerySampleRunData run)
    {
        var answer = run.Answer;
        var declines = answer.Contains("read-only", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("read only", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("can't", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unable to", StringComparison.OrdinalIgnoreCase);
        var claimsToHaveWritten = answer.Contains("I've updated", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I have updated", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I've fixed", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I have fixed", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("I've added", StringComparison.OrdinalIgnoreCase);

        var checks = new Dictionary<string, bool>
        {
            ["declines"] = declines,
            ["does_not_claim_write"] = !claimsToHaveWritten,
        };
        return new SampleScore(declines && !claimsToHaveWritten, false, checks);
    }
}
