namespace Grimoire.IngestAgent.Guardrails;

/// <summary>
/// One policy refusal, persisted into the task artifact (FR-008, SC-002).
/// </summary>
public sealed record DeniedActionRecord(
    string Action,
    string Target,
    string Reason,
    int Turn);
