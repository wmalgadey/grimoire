namespace Grimoire.AgentRuntime.Guardrails;

/// <summary>
/// One policy refusal, persisted into the task artifact (FR-008, SC-002).
/// <c>Reason</c> is one of: <c>no_rule</c>, <c>out_of_scope</c>, <c>traversal</c> (pure
/// <see cref="Grimoire.Domain.Guardrails.SafetyPolicy"/> scope decisions), or, since
/// ADR-015 (012-query-synthesis-writes), one of the write-coordination reasons produced
/// by <see cref="Coordination.SharedFileWriteGuard"/>: <c>create_only_target_exists</c>
/// (the matched write rule is create-only and the canonical target already exists),
/// <c>write_conflict_stale_read</c> (the target's on-disk content no longer matches this
/// run's last-read hash for it — re-read and retry), or
/// <c>write_coordination_timeout</c> (lock acquisition exceeded the bounded backoff cap).
/// </summary>
public sealed record DeniedActionRecord(
    string Action,
    string RequestedTarget,
    string CanonicalTarget,
    string Reason,
    int Turn);
