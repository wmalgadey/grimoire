namespace Grimoire.Hub.OperationalState;

/// <param name="Attempt">
/// 023-task-ui-improvements (data-model.md §2, ADR-025): reactivation attempts already
/// spent on the current run occupancy. Reset to 0 on a normal start and on a manual
/// restart; incremented per liveness-interruption-driven re-launch. Transient like the
/// rest of the row (deleted in <c>FinishRunAsync</c>) — the durable record of an attempt
/// is the <c>ingest_status_history</c> entry's detail text.
/// </param>
public sealed record OperationalTaskState(
    string TaskId, string Status, int? ProcessId, DateTimeOffset UpdatedAt, int Attempt = 0);
