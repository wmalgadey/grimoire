namespace Grimoire.IngestAgent.TaskArtifact;

/// <summary>
/// Identity record for an instruction file loaded during a run (FR-012).
/// </summary>
public sealed record InstructionFileRecord(string Path, string Sha256);

/// <summary>
/// Identity record for the safety policy file loaded during a run (FR-012).
/// </summary>
public sealed record PolicyRecord(string Path, int Version, string Sha256);

/// <summary>
/// One policy denial recorded in the task artifact (FR-008, SC-002).
/// </summary>
public sealed record DeniedActionEntry(
    string Action,
    string RequestedTarget,
    string CanonicalTarget,
    string Reason,
    int Turn);

/// <summary>
/// Per-run task artifact document (v2 — agentic core).
/// Replaces <c>pages_touched</c> with three action-specific lists and adds
/// agentic governance fields (FR-012, SC-001, SC-002, SC-005).
/// </summary>
public sealed record TaskArtifactDocument(
    string TaskId,
    string Type,
    string Status,
    string Agent,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string SourceRef,
    // v1 compat: kept for Hub restart reconciliation path that reads existing artifacts
    IReadOnlyList<string> PagesTouched,
    string? FailureReason,
    string Narrative,
    // v2 fields
    IReadOnlyList<string>? PagesCreated = null,
    IReadOnlyList<string>? PagesUpdated = null,
    IReadOnlyList<string>? PagesSuperseded = null,
    IReadOnlyList<DeniedActionEntry>? DeniedActions = null,
    IReadOnlyList<InstructionFileRecord>? InstructionFiles = null,
    PolicyRecord? Policy = null,
    string? Model = null,
    int? Turns = null,
    bool? RolledBack = null);
