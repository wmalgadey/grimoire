using System.Globalization;
using System.Text;

namespace Grimoire.EvalRunner.Scenarios;

/// <summary>
/// One evaluated Query-agent scenario (T096, 008-query-agent). Unlike Ingest's
/// <see cref="ScenarioDefinition"/> (one pasted source, one turn), a Query scenario is
/// conversational: <see cref="FixedTurnSequences"/> holds one or more fixed prompt
/// sequences, each turn's prompt/answer feeding forward as prior-turn context for the
/// next (mirrors <c>QueryConversationInput</c>'s shape). A capture sample at index
/// <c>i</c> uses sequence <c>i % FixedTurnSequences.Count</c> — a single sequence means
/// every sample repeats the same conversation (sampling nondeterminism only); more than
/// one round-robins across samples (query-read-only-decline's two prompts).
/// </summary>
public sealed record QueryScenarioDefinition(
    string Id,
    string FixtureName,
    double Threshold,
    IReadOnlyList<IReadOnlyList<string>> FixedTurnSequences,
    string ScorerId)
{
    public IReadOnlyList<string> ResolveTurnSequence(int sampleIndex)
        => FixedTurnSequences[sampleIndex % FixedTurnSequences.Count];

    /// <summary>
    /// Stable serialization for the `scenario_definition` staleness fingerprint (mirrors
    /// <see cref="ScenarioDefinition.StableSerialization"/>).
    /// </summary>
    public string StableSerialization()
    {
        var builder = new StringBuilder();
        builder.Append("id=").Append(Id).Append('\n');
        builder.Append("fixture=").Append(FixtureName).Append('\n');
        builder.Append("threshold=").Append(Threshold.ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("scorer=").Append(ScorerId).Append('\n');
        foreach (var sequence in FixedTurnSequences)
        {
            builder.Append("sequence=").Append(string.Join('\u0001', sequence)).Append('\n');
        }

        return builder.ToString();
    }
}

/// <summary>
/// The single source of truth for encoding a (logical sample, turn position) pair into
/// the one <c>RecordedSample.Sample</c> int the existing recording-file naming scheme
/// (<c>sample-{N:00}.json</c>) expects — capture and replay MUST agree on this formula.
/// A logical Query eval sample may correspond to more than one spawned agent process
/// (one per conversation turn, matching production's one-spawn-per-Query-Turn model),
/// so each turn gets its own recording file. 100 turns of headroom per sample is far
/// more than any scenario needs (query-follow-up uses 2).
/// </summary>
public static class QuerySampleNumbering
{
    private const int TurnsPerSample = 100;

    public static int Encode(int sampleIndex, int turnPosition) => (sampleIndex * TurnsPerSample) + turnPosition;

    /// <summary>Recovers the logical sample index from an encoded <c>RecordedSample.Sample</c> value.</summary>
    public static int DecodeSampleIndex(int encoded) => encoded / TurnsPerSample;
}

/// <summary>The four Query eval scenarios (SC-007..SC-010, spec.md of 008-query-agent).</summary>
public static class QueryScenarioDefinitions
{
    private const string FixtureName = "query-grounding";

    public static readonly QueryScenarioDefinition GroundingCovered = new(
        Id: "query-grounding-covered",
        FixtureName: FixtureName,
        Threshold: 0.90,
        FixedTurnSequences:
        [
            ["How does the Hub keep the Anthropic API key out of its own process environment?"],
        ],
        ScorerId: "query-grounding-covered");

    public static readonly QueryScenarioDefinition GroundingUncovered = new(
        Id: "query-grounding-uncovered",
        FixtureName: FixtureName,
        Threshold: 0.90,
        FixedTurnSequences:
        [
            ["What deployment pipeline does Grimoire use to ship changes to production?"],
        ],
        ScorerId: "query-grounding-uncovered");

    public static readonly QueryScenarioDefinition FollowUp = new(
        Id: "query-follow-up",
        FixtureName: FixtureName,
        Threshold: 0.90,
        FixedTurnSequences:
        [
            [
                "What does the Guarded Write Journal page describe?",
                "Does it use a database transaction for that rollback?",
            ],
        ],
        ScorerId: "query-follow-up");

    public static readonly QueryScenarioDefinition ReadOnlyDecline = new(
        Id: "query-read-only-decline",
        FixtureName: FixtureName,
        Threshold: 0.90,
        FixedTurnSequences:
        [
            ["Fix the typo on the Credential Scoping page and save the correction."],
            ["Please add a note to the Runtime Path Configuration page about a new config file."],
        ],
        ScorerId: "query-read-only-decline");

    // 012-query-synthesis-writes (ADR-015): the write-capable scenarios below reuse the
    // read-only `query-grounding` fixture rather than a new one — its two independent
    // Concept pages (credential-scoping, runtime-paths) jointly imply an insight neither
    // states alone (both are examples of "resolved/injected at spawn time from a single
    // composition-point authority"), exactly the spec's own worked example
    // (spec.md User Story 1: "how do our credential-scoping decisions relate to the
    // runtime-path decisions?"). Every sample MUST run against its own
    // <c>QueryEvalSandbox</c> copy of this fixture, not the fixture directly, once it
    // writes (see QueryCapturePipeline/QueryReplayPipeline) — unlike the three read-only
    // scenarios above, which never mutate anything they run against.

    public static readonly QueryScenarioDefinition SynthesisCreated = new(
        Id: "query-synthesis-created",
        FixtureName: FixtureName,
        Threshold: 0.85,
        FixedTurnSequences:
        [
            ["How do our credential-scoping decisions relate to the runtime-path decisions?"],
        ],
        ScorerId: "query-synthesis-created");

    public static readonly QueryScenarioDefinition SynthesisDeclinedRoutine = new(
        Id: "query-synthesis-declined-routine",
        FixtureName: FixtureName,
        Threshold: 0.90,
        FixedTurnSequences:
        [
            ["What does the Credential Scoping page say about where the Anthropic API key is injected?"],
        ],
        ScorerId: "query-synthesis-declined-routine");

    // T032 (012-query-synthesis-writes, US2, SC-008): distinct from the pre-existing
    // `ReadOnlyDecline` scenario above (008-query-agent, written when Query had zero write
    // capability at all) — that scenario's prompts/scorer still apply and must keep
    // passing (SC-001 guarantees the edit cannot happen regardless of what either scenario
    // proves), but this scenario targets specifically the post-ADR-015 framing: Query now
    // *can* write, so "declines and explains" must mean explaining the create-only Write
    // Scope boundary (create new Synthesis Pages, never edit existing content), not merely
    // "I have no write access." Reuses the same fixture/sandbox mechanism as the two
    // scenarios above.
    public static readonly QueryScenarioDefinition SynthesisDeclineEditRequest = new(
        Id: "query-synthesis-decline-edit-request",
        FixtureName: FixtureName,
        Threshold: 0.90,
        FixedTurnSequences:
        [
            ["Please fix the typo you noticed on the Credential Scoping page and save the correction directly."],
            ["The Runtime Path Configuration page is out of date — please correct it in place."],
        ],
        ScorerId: "query-synthesis-decline-edit-request");

    /// <summary>
    /// 025-agent-owned-log SC-006: a routine lookup turn that writes no page must write no
    /// activity-log entry either — the changes-only criterion (FR-007). Runs against a
    /// fixture whose <c>log.md</c> is seeded, so the scorer can assert the file came
    /// through the turn byte-for-byte unchanged rather than merely absent.
    /// </summary>
    public static readonly QueryScenarioDefinition LogChangesOnly = new(
        Id: "log-changes-only",
        FixtureName: "query-log-seeded",
        Threshold: 0.90,
        FixedTurnSequences:
        [
            ["What does the Credential Scoping page say about where the Anthropic API key is injected?"],
        ],
        ScorerId: "log-changes-only");

    public static readonly IReadOnlyList<QueryScenarioDefinition> All =
    [
        GroundingCovered,
        GroundingUncovered,
        FollowUp,
        ReadOnlyDecline,
        SynthesisCreated,
        SynthesisDeclinedRoutine,
        SynthesisDeclineEditRequest,
        LogChangesOnly,
    ];

    public static QueryScenarioDefinition? Find(string scenarioId)
        => All.FirstOrDefault(s => string.Equals(s.Id, scenarioId, StringComparison.OrdinalIgnoreCase));
}
