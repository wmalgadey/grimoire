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
/// <c>write_coordination_timeout</c> (lock acquisition exceeded the bounded backoff cap);
/// or, since ADR-016 (013-lint-agent), one of the frontmatter-only reasons also produced
/// by <see cref="Coordination.SharedFileWriteGuard"/>: <c>frontmatter_only_target_missing</c>
/// (the matched write rule is frontmatter-only and the canonical target does not exist),
/// <c>frontmatter_only_malformed_document</c> (the current on-disk content or the proposed
/// content is not a well-formed two-delimiter frontmatter document), or
/// <c>frontmatter_only_body_changed</c> (the content after the closing <c>---</c> differs
/// between current and proposed); or, since ADR-017 (014-wiki-storage-restructure), one of
/// the <c>log.md</c> format-validation reasons also produced by
/// <see cref="Coordination.SharedFileWriteGuard"/>: <c>log_entry_not_appended</c> (the
/// proposed <c>log.md</c> content does not extend the current on-disk content
/// byte-for-byte — the file must stay append-only, FR-011), <c>log_entry_malformed_heading</c>
/// (the appended tail's first non-blank line does not match
/// <c>^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$</c>), or <c>log_entry_missing_paragraph</c> (a
/// heading was appended with no following non-blank paragraph line); or, since ADR-017's
/// US4 extension (014-wiki-storage-restructure), <c>catalog_entry_malformed</c> (a newly
/// added <c>index.md</c> line starting with <c>- [</c> — present in the proposed content
/// but absent, byte-for-byte, from the current content — does not match
/// <c>^- \[.+\]\(.+\) — .+ — .+$</c>); or, since ADR-023 (022-align-wiki-structure Phase
/// 5), <c>harness_surface_not_granted</c> (a read-scope target — <c>list_files</c> or
/// <c>read_file</c> — falls under one of the four reserved harness surfaces
/// (<c>tasks/</c>, <c>conversations/</c>, <c>findings/</c>, <c>remediation-tasks/</c>)
/// the operator has not granted via <c>Grimoire:HarnessSurfaceReads</c>; checked before
/// <see cref="Grimoire.Domain.Guardrails.SafetyPolicy"/>'s read allow loop, distinct from
/// <c>no_rule</c> so an operator can distinguish "you have not granted this" from "this
/// is outside the policy" — SC-010).
/// </summary>
public sealed record DeniedActionRecord(
    string Action,
    string RequestedTarget,
    string CanonicalTarget,
    string Reason,
    int Turn);
