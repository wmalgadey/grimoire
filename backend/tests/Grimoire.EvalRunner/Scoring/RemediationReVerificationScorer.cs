namespace Grimoire.EvalRunner.Scoring;

/// <summary>
/// The remediation-execution mode's terminal-event outcome, as reported by the run
/// (T039, 015-lint-board-parity, FR-018). Mirrors <see cref="LintSampleRunData"/>'s role
/// for the lint-run scorers — everything a scorer may inspect about one sample.
/// </summary>
public sealed record RemediationReVerificationSampleRunData(
    string? RemediationOutcome,
    string? Reason,
    string WikiRoot = "");

/// <summary>
/// T039 (015-lint-board-parity, FR-018): the deterministic scorer for the
/// remediation-execution mode's re-verification eval. Unlike the former
/// proposal-relevance scorer (T028, removed with its lower-stakes scenario), which
/// stood in for subjective human review via a frozen golden set (research.md R6's
/// caveat), this scorer needs no such stand-in: each fixture's
/// <see cref="Scenarios.RemediationReVerificationScenarioDefinition.ExpectedOutcome"/> is
/// an objective fact the fixture authors control directly — "does the wiki state this
/// scenario replays against still contain the problem the proposal describes" is
/// something the fixture's author knows for certain, not a matter of taste. Scoring is
/// therefore a straight equality check against what the agent actually reported on its
/// terminal event (contracts/remediation-lifecycle-events.md `remediationOutcome`),
/// never a judgment made here (Constitution Principle V — the judgment stays entirely in
/// data/agents/lint/system-prompt.md's "Remediation Execution Mode" section, T036).
/// </summary>
public static class RemediationReVerificationScorer
{
    public const string ScorerId = "remediation-reverification-outcome-matches-fixture";

    /// <summary>
    /// T067 (026-guarded-tool-surface, SC-013): the authorized body-edit scorer. Scored
    /// separately from <see cref="ScorerId"/> because the question is different — not "did
    /// the agent reach the right verdict about whether to act", but "having been authorized
    /// to act, did the change it made actually address the proposal".
    /// </summary>
    public const string BodyEditScorerId = "remediation-body-edit-addresses-proposal";

    /// <summary>The value the fixture page states in its body, which the proposal corrects.</summary>
    public const string BodyEditStaleValue = "30 seconds";

    /// <summary>The value the authorized proposal directs the agent to state instead.</summary>
    public const string BodyEditCorrectedValue = "300 seconds";

    private const string BodyEditPageRelativePath = "cache-ttl.md";

    /// <summary>
    /// SC-013: "≥ 90% of sampled authorized body-edit remediations produce a page change
    /// that a reviewer scores as addressing the authorized proposal."
    ///
    /// <para><b>Why this is scoreable without a human reviewer</b>, on the same grounds
    /// <see cref="ScorerId"/>'s doc comment gives for the re-verification pair: the fixture
    /// author chose the proposal and the page content together, so "addresses the proposal"
    /// is an objective fact about the resulting file rather than a matter of taste. The
    /// proposal names one wrong value and the value that replaces it; a change that made
    /// that replacement addresses it, and one that did not, did not.</para>
    ///
    /// <para><b>What is deliberately not asserted</b> is the wording around the change.
    /// How the agent phrases the corrected sentences is its judgment (Principle V); this
    /// scorer checks only that the stale value is gone, the corrected value is present, the
    /// page survived as a page, and the frontmatter the proposal never mentioned came
    /// through untouched — the last of these being the thing body-edit authority makes
    /// possible to get wrong, and therefore the thing worth checking.</para>
    /// </summary>
    public static SampleScore ScoreBodyEdit(RemediationReVerificationSampleRunData run)
    {
        var pagePath = Path.Combine(run.WikiRoot, BodyEditPageRelativePath);
        var pageExists = File.Exists(pagePath);
        var content = pageExists ? File.ReadAllText(pagePath) : string.Empty;

        var actualOutcome = string.IsNullOrWhiteSpace(run.RemediationOutcome) ? "applied" : run.RemediationOutcome;

        var checks = new Dictionary<string, bool>
        {
            ["reported_applied"] = string.Equals(actualOutcome, "applied", StringComparison.Ordinal),
            ["page_still_exists"] = pageExists,
            ["stale_value_removed"] = pageExists && !content.Contains(BodyEditStaleValue, StringComparison.OrdinalIgnoreCase),
            ["corrected_value_present"] = content.Contains(BodyEditCorrectedValue, StringComparison.OrdinalIgnoreCase),
            ["heading_preserved"] = content.Contains("# Cache TTL Defaults", StringComparison.Ordinal),
            // The paragraph the proposal says nothing about, byte-for-byte. This is the
            // check that actually stands in for ADR-016's superseded structural guarantee:
            // the guard no longer refuses body changes, so "the rest of the body survived"
            // is now an agent-judgment property rather than a harness-enforced one.
            ["untouched_body_preserved"] = content.Contains(
                "Cache entries written through the batch import path are exempt and never expire.",
                StringComparison.Ordinal),
            // The proposal says nothing about tags or confidence; a body edit that also
            // rewrote them exceeded its authorization even though the guard permitted it.
            ["unrelated_frontmatter_preserved"] =
                content.Contains("concept/Caching", StringComparison.Ordinal)
                && content.Contains("confidence: medium", StringComparison.Ordinal),
        };

        return new SampleScore(checks.Values.All(v => v), false, checks);
    }

    public static SampleScore Score(string expectedOutcome, RemediationReVerificationSampleRunData run)
    {
        // Absent `remediationOutcome` on a `completed` event means "applied" per the
        // contract (contracts/remediation-lifecycle-events.md) — normalize before
        // comparing, mirroring RemediationRunCoordinator's own Hub-side mapping.
        var actualOutcome = string.IsNullOrWhiteSpace(run.RemediationOutcome)
            ? "applied"
            : run.RemediationOutcome;

        var outcomeMatches = string.Equals(actualOutcome, expectedOutcome, StringComparison.Ordinal);

        // FR-018/SC-007: a `not_applicable` outcome must always carry a genuine reason —
        // an empty one is a reporting defect even when the outcome itself is correct.
        var reasonPresentWhenRequired = expectedOutcome != "not_applicable"
            || !string.IsNullOrWhiteSpace(run.Reason);

        var checks = new Dictionary<string, bool>
        {
            ["outcome_matches_fixture"] = outcomeMatches,
            ["reason_present_when_required"] = reasonPresentWhenRequired,
        };

        return new SampleScore(outcomeMatches && reasonPresentWhenRequired, false, checks);
    }
}
