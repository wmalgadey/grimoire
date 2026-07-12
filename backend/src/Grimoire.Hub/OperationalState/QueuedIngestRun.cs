namespace Grimoire.Hub.OperationalState;

/// <summary>
/// One accepted-but-not-started task in the persistent Run Queue (data-model.md Run
/// Queue, ADR-008). FIFO order authority is <see cref="AcceptedAt"/>; the row carries
/// what the coordinator needs to rebuild the agent request after a Hub restart.
/// </summary>
public sealed record QueuedIngestRun(
    string TaskId,
    DateTimeOffset AcceptedAt,
    string SourceRef,
    string? UserPrompt);
