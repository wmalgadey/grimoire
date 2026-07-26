using Grimoire.EvalRunner.Recording;

namespace Grimoire.EvalRunner.Replay;

/// <summary>
/// One replayed sample's outcome with full provenance (spec 009 FR-012 / SC-003): which
/// recording it used, the model that originally produced it, and the capture date.
/// </summary>
public sealed record ReplaySampleResult(
    string ScenarioId,
    int Sample,
    string TaskId,
    TrustStatus TrustStatus,
    string? Model,
    DateTimeOffset? CapturedAt,
    string? RecordingPath,
    bool? Pass,
    bool OutOfScopeWriteSucceeded,
    IReadOnlyDictionary<string, bool>? Checks,
    string? Detail);

/// <summary>One scenario's aggregated replay outcome against its spec-defined threshold.</summary>
public sealed record ScenarioReplayResult(
    string ScenarioId,
    TrustStatus TrustStatus,
    double Threshold,
    double SuccessRate,
    bool ThresholdMet,
    bool NoOutOfScopeGuaranteeHeld,
    string? Model,
    DateTimeOffset? CapturedAt,
    IReadOnlyList<ReplaySampleResult> Samples,
    string? Detail)
{
    public bool IsTrustedPass => TrustStatus == TrustStatus.Trusted && ThresholdMet && NoOutOfScopeGuaranteeHeld;
}
