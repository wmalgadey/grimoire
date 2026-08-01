namespace Grimoire.EvalRunner.Scoring;

/// <summary>
/// The human-adjudicated golden set for <c>lint-remediation-proposals</c> (015-lint-
/// board-parity T028, SC-006, research.md "Two eval suites ... scored against a
/// human-adjudicated golden set"). Frozen against the <c>lint-seeded-defects</c> fixture
/// (<c>backend/tests/Grimoire.AgentEvals/Fixtures/lint-seeded-defects/wiki/</c>, the same
/// fixture <see cref="Scenarios.LintScenarioDefinitions.DefectsFound"/>/
/// <c>GenuineFindings</c>/<c>MetadataProposals</c> already score against): of the
/// fixture's six seeded defects, this table records which ones a reviewing human would
/// accept as a relevant, actionable remediation proposal versus reject as irrelevant —
/// mirroring `agents/lint/system-prompt.md`'s own actionable/informational split (Step
/// 3b: "Informational findings produce no proposal").
///
/// <b>Caveat — this is scaffolding, not a substitute for human review.</b> SC-006 asks
/// for judgment by "a reviewing user"; a golden set fixed in source is a proxy a CI
/// pipeline can check deterministically, adjudicated once here (by inspecting the
/// fixture pages and `agents/lint/system-prompt.md`'s own actionable/informational
/// rules) rather than by a live human reviewing live agent output. Before this scenario
/// is captured against real model output (see
/// `docs/befunde-remediation-prompts.md`-style capture workflow /
/// <c>LintStalenessCheck.RefreshCommand</c>), a human should re-confirm this table
/// against the fixture and adjust it if the agent's actual proposals reveal a legitimate
/// edge case this table did not anticipate (e.g. the contradiction pair being split into
/// two proposals instead of one — both targets are still in
/// <see cref="SeededDefectsActionablePages"/>, so that split scores fine).
/// </summary>
public static class RemediationGoldenSet
{
    /// <summary>
    /// Pages a relevant, actionable proposal may target — five of the fixture's six
    /// seeded defects: the contradiction pair (Content Quality: reconcile the two
    /// claims), the missing-cross-reference pair (Content Quality: link them), the orphan
    /// page (Structure: link it from somewhere), and the two metadata-hygiene pages
    /// (missing tags, missing confidence — each proposes a specific fix per
    /// `agents/lint/system-prompt.md`'s Metadata Hygiene section).
    /// </summary>
    public static readonly IReadOnlyList<string> SeededDefectsActionablePages =
    [
        "cache-invalidation-ttl", "cache-invalidation-events",
        "retry-backoff", "circuit-breaker",
        "orphan-topic",
        "undertagged-topic",
        "unscored-topic",
    ];

    /// <summary>
    /// The fixture's one seeded defect that is explicitly informational, never a
    /// proposal target: a Review Window candidate (`stale-topic`, low confidence +
    /// `last_reviewed` far outside the window) — `agents/lint/system-prompt.md` lists
    /// Review candidates as "an informational sub-section — they are not errors", and
    /// Step 3b instructs "Informational findings produce no proposal." A proposal
    /// targeting this page is a relevance failure, not a genuine remediation.
    /// </summary>
    public const string InformationalOnlyPage = "stale-topic";
}
