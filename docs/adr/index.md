# ADR Index

Central overview of all Architecture Decision Records, per Constitution Principle III
("ADR Status Maintenance"). This file MUST be updated in the same change as any ADR
whose status or existence changes — it is the single place to see which ADRs currently
govern the codebase without opening every file.

Status is one of exactly: `Accepted`, `Proposed`, `Declined`, `Deprecated`, `Superseded`
(case-insensitive — ADR files' YAML frontmatter uses lowercase, e.g. `status: accepted`;
this table uses Title Case for readability, not a distinct value). Per Constitution v2.0.0
("ADR Status Maintenance"), every ADR carries its links in frontmatter (`supersedes` /
`superseded_by` / `reason`, per `docs/adr/TEMPLATE.md`) and supersession is always
whole-ADR, never partial — the `Amends`/`Amended by` link is retired for new ADRs.

**The v2.0.0 restructuring pass (ADR-035 through ADR-050).** The pre-v2.0.0 collection
had accumulated partial amendments — ADRs whose current meaning required walking an
amendment chain instead of reading one Accepted document. This pass executed the
retroactive cleanup the v2.0.0 Sync Impact Report deferred: the seven worst
multi-aspect/partially-amended carriers (ADR-002, ADR-008, ADR-009, ADR-011, ADR-020,
ADR-022, ADR-028) were wholly superseded by sixteen new single-aspect ADRs (ADR-035
through ADR-050), and ADR-017 was deprecated without replacement as feature-scoped
format content. The `Supersedes / Amends` chain entries on pre-v2.0.0 rows below are
historical record under Governance's non-retroactivity clause — accurate as history,
not a pattern for new ADRs to follow.

An ADR may also **extend** another: it builds on that decision without replacing any part
of it. `Extends` is listed in the chain columns for context but is not a supersede link —
Constitution v2.0.0's "Extension is not invalidation" makes this distinction binding.

| ADR | Title | Status | Supersedes / Amends | Superseded by / Amended by |
| --- | --- | --- | --- | --- |
| [ADR-001](ADR-001-backend-frontend-tech-stack.md) | Backend and Frontend Technology Stack | Accepted | — | — |
| [ADR-002](ADR-002-ingest-agent-execution-model.md) | Ingest Agent Execution Model | **Superseded** | — | **Superseded by ADR-036** (whole-ADR, v2.0.0 restructuring); historical partial amendments by ADR-008, ADR-009, ADR-022, ADR-025 |
| [ADR-003](ADR-003-domain-operational-state-persistence.md) | Domain vs. Operational State Persistence | Accepted | — | Superseded in part by ADR-009 (historical; the naming detail — the substantive split stands) |
| [ADR-004](ADR-004-credential-scoping.md) | Credential Scoping for the LLM API Key | Accepted | — | Amended by ADR-009 (historical) |
| [ADR-005](ADR-005-observability-backend.md) | Observability Backend (Local and CI) | Accepted | — | — |
| [ADR-006](ADR-006-agent-tool-loop-guarded-boundary.md) | Agent Tool-Use Loop and Guarded Tool Boundary | Accepted | — | Amended by ADR-009, ADR-030 (tool surface widened past three tools), ADR-031 (write journal covers deletion) — all historical |
| [ADR-007](ADR-007-agent-instruction-surface.md) | Agent Instruction Surface — Single System Prompt and Versioned Default User Prompt | Accepted | — | Amended by ADR-009, ADR-022, ADR-029 (harness operator turns only) — all historical |
| [ADR-008](ADR-008-agent-event-channel-run-supervision.md) | Agent Event Channel, Run Supervision, and Persistent Run Queue | **Superseded** | Amends ADR-002 (historical) | **Superseded by ADR-037, ADR-038, ADR-039** (whole-ADR, v2.0.0 restructuring); historical partial amendments by ADR-009, ADR-025 |
| [ADR-009](ADR-009-runtime-path-configuration.md) | Explicit Runtime Path Configuration and Consolidated Data Directory | **Superseded** | Supersedes ADR-003 (in part); amends ADR-002, ADR-004, ADR-006, ADR-007, ADR-008 (historical) | **Superseded by ADR-040** (whole-ADR, v2.0.0 restructuring); previously superseded in part by ADR-022 |
| [ADR-010](ADR-010-hexagonal-ports-adapter-namespaces.md) | Hexagonal Ports and Adapter Namespaces for External Systems | Accepted | — | Amended by ADR-011 (`IModelClient` port row only — historical) |
| [ADR-011](ADR-011-query-agent-shared-runtime-and-concurrency-model.md) | Shared Agent Runtime, Streaming, and Query Concurrency Model | **Superseded** | Amends ADR-010 (historical) | **Superseded by ADR-044, ADR-045, ADR-046, ADR-047** (whole-ADR, v2.0.0 restructuring); aspects previously carved out to ADR-013 (packaging), ADR-014 (persistence), ADR-015 (write scope), ADR-030 (registry) |
| [ADR-012](ADR-012-eval-runner-recorded-replay.md) | Standalone Eval Runner and Recorded-Replay at the Model Port | Accepted | — | Amended by ADR-022 (historical) |
| [ADR-013](ADR-013-unified-agent-platform-packaging-and-naming.md) | Unified Agent Platform Packaging and Agent-Artifact Naming Convention | Accepted | Amends ADR-011 (packaging / runtime-sharing aspects only — historical) | — |
| [ADR-014](ADR-014-query-conversation-records.md) | Query Conversation Records and Record-Sourced Follow-Up Context | Accepted | Supersedes ADR-011 ("Persistence and conversation context" section only — historical) | — |
| [ADR-015](ADR-015-query-write-scope-and-wiki-write-coordination.md) | Query Agent Write Scope and Cross-Process Wiki Write Coordination | Accepted | Supersedes ADR-011 ("Query is structurally write-free" framing only — historical) | — |
| [ADR-016](ADR-016-lint-write-scope-frontmatter-only-enforcement.md) | Lint Write Scope — Structural Frontmatter-Only Enforcement | **Superseded** | Extends ADR-015 (no supersession) | **Superseded by ADR-031** (decision fully retired; `FrontmatterOnly` mode retained in the model) |
| [ADR-017](ADR-017-log-and-catalog-entry-format-enforcement.md) | Structural Format Enforcement for `log.md` and `index.md` Entries | **Deprecated** | Extends ADR-006, ADR-015, ADR-016 (no supersession) | **Deprecated without replacement** (v2.0.0 restructuring): feature-scoped format content owned by the specs/014 and specs/025 contracts — see frontmatter `reason` |
| [ADR-018](ADR-018-remediation-action-authorization-and-execution.md) | Human-Authorized Remediation Action Execution | Accepted | — | Amended by ADR-031 (authorization gates the run, no longer the write authority — historical) |
| [ADR-019](ADR-019-devcontainer-host-runtime-and-credential-access.md) | Devcontainer Host Container-Runtime and Credential Access | Accepted | — | Amended by ADR-022 (historical) |
| [ADR-020](ADR-020-hub-cli-command-surface.md) | Hub CLI Command Surface — Framework, Dispatch, and In-Process Blocking Execution | **Superseded** | — | **Superseded by ADR-048, ADR-049, ADR-050** (whole-ADR, v2.0.0 restructuring); historical partial amendments by ADR-022, ADR-023 |
| [ADR-021](ADR-021-test-tier-taxonomy-and-deterministic-wait-enforcement.md) | Backend Test Tier Taxonomy, Deterministic-Wait Convention, and Suite Concurrency Levers | Accepted | — | Amended by ADR-033 (SlowEval replay class set — historical) |
| [ADR-022](ADR-022-minimal-directory-configuration-surface.md) | Minimal Directory Configuration Surface — Three Roots, Mandatory Configuration File, and Build-Distributed Agent Artifacts | **Superseded** | Amends ADR-002, ADR-007, ADR-012, ADR-019, ADR-020; supersedes ADR-009 (in part) — historical | **Superseded by ADR-041, ADR-042, ADR-043** (whole-ADR, v2.0.0 restructuring); historical partial amendments by ADR-024, ADR-032 |
| [ADR-023](ADR-023-hub-cli-default-command-and-root-help-routing.md) | Hub CLI Default Command and Root Help Routing | Accepted | Amends ADR-020 (historical) | — |
| [ADR-024](ADR-024-memory-directory-root.md) | Memory Directory — A Fourth Independent Root for Agent Process Bookkeeping | Accepted | Amends ADR-022 (R1 switch cap, sub-path anchoring — historical; the root itself is the ADR-041 extension precedent) | Amended by ADR-032 (M1/M2/M4 enforcement mechanism — historical) |
| [ADR-025](ADR-025-ingest-task-lifecycle-reentry.md) | Ingest Task Lifecycle Re-Entry — Liveness Reactivation, Manual Restart, and Status History | Accepted | Amends ADR-002 (retry/backoff deferral revoked for the liveness case), ADR-008 (liveness consequence, terminal-state re-entry) — historical | — |
| [ADR-026](ADR-026-hub-api-error-contract-and-frontend-error-presentation.md) | Hub API Error Response Contract and Shared Frontend Error Presentation | Accepted | Extends ADR-020 (HTTP counterpart to the CLI failure contract), ADR-013 (registers `Grimoire.Hub.ApiErrors` in the N1 ownership map) — no supersession | — |
| [ADR-027](ADR-027-gitversion-github-flow.md) | Version Numbers Computed by GitVersion, Branching by GitHub Flow | Accepted | — | — |
| [ADR-028](ADR-028-agent-owned-activity-log-prepend-ordering.md) | Agent-Owned Activity Log — Prepend-Only Ordering and Removal of Harness Authorship | **Superseded** | Amends ADR-017 (`log.md` ordering and backstop bullet only — historical) | **Superseded by ADR-035** (whole-ADR, v2.0.0 restructuring; the ordering half is feature-scoped, owned by the specs/025 write contract) |
| [ADR-029](ADR-029-harness-operator-turn-delimiter.md) | Harness Operator Turns Are Delimited Inside the User Channel | Accepted | Amends ADR-007 (adds the harness operator turn to the instruction surface — historical) | — |
| [ADR-030](ADR-030-guarded-retrieval-tool-surface.md) | Guarded Retrieval Tools — Search, Ranged Read, and Read-Only Batch | Accepted | Amends ADR-006 (tool surface), ADR-011 (registry) — historical | — |
| [ADR-031](ADR-031-lint-full-wiki-write-scope.md) | Lint Holds Full Authority Over Wiki Content, in Both Modes | Accepted | Supersedes ADR-016; amends ADR-017, ADR-018, ADR-006 (journal covers deletion) — historical | — |
| [ADR-032](ADR-032-behavioral-enforcement-for-path-surface-invariants.md) | Behavioral Enforcement for Feature-Scoped Path-Surface Invariants | Accepted | Amends ADR-022 (R2 enforcement), ADR-024 (M1/M2/M4 enforcement) — historical | — |
| [ADR-033](ADR-033-sloweval-replay-class-set-reduction.md) | SlowEval Replay Class Set Reduced by the Lower-Stakes Eval Removal | Accepted | Amends ADR-021 (SlowEval class enumeration — historical) | — |
| [ADR-034](ADR-034-path-and-subprocess-containment-hardening.md) | Path and Subprocess Containment Hardening | Accepted | — | — |
| [ADR-035](ADR-035-agent-exclusive-activity-log-authorship.md) | Agent-Exclusive Authorship of the Wiki Activity Log | Accepted | Supersedes ADR-028 | — |
| [ADR-036](ADR-036-agent-child-process-spawn-contract.md) | Agent Child-Process Spawn Contract | Accepted | Supersedes ADR-002 | — |
| [ADR-037](ADR-037-agent-event-channel-protocol.md) | Agent Event Channel Protocol — NDJSON Event Stream on stdout | Accepted | Supersedes ADR-008 | — |
| [ADR-038](ADR-038-heartbeat-run-supervision.md) | Heartbeat Liveness as the Sole Run-Failure-Detection Authority | Accepted | Supersedes ADR-008 | — |
| [ADR-039](ADR-039-persistent-run-queue.md) | Persistent FIFO Run Queue in the Operational-State Database | Accepted | Supersedes ADR-008 | — |
| [ADR-040](ADR-040-runtime-path-composition.md) | Runtime Path Composition at One Explicit Configuration Point with Fail-Fast Validation | Accepted | Supersedes ADR-009 | — |
| [ADR-041](ADR-041-independent-directory-roots.md) | Independent Cwd-Anchored Directory Roots | Accepted | Supersedes ADR-022 | — |
| [ADR-042](ADR-042-mandatory-configuration-file.md) | Mandatory Configuration File as the Sole Source of Configuration Defaults | Accepted | Supersedes ADR-022 | — |
| [ADR-043](ADR-043-build-distributed-agent-artifacts.md) | Build-Distributed Agent Artifacts and Single Launch Mode | Accepted | Supersedes ADR-022 | — |
| [ADR-044](ADR-044-shared-agent-runtime-library.md) | Shared Agent Runtime Library | Accepted | Supersedes ADR-011 | — |
| [ADR-045](ADR-045-token-level-answer-streaming.md) | Token-Level Answer Streaming over the Agent Event Channel | Accepted | Supersedes ADR-011 | — |
| [ADR-046](ADR-046-query-dispatch-and-bounded-concurrency.md) | Query Dispatch — Bounded Concurrency, Immediate Rejection, and Interruption | Accepted | Supersedes ADR-011 | — |
| [ADR-047](ADR-047-query-realtime-delivery.md) | Query Realtime Delivery over a Dedicated SignalR Connection | Accepted | Supersedes ADR-011 | — |
| [ADR-048](ADR-048-hub-cli-framework.md) | Hub CLI Command Framework — Spectre.Console.Cli | Accepted | Supersedes ADR-020 | — |
| [ADR-049](ADR-049-cli-in-process-blocking-execution.md) | Hub CLI In-Process Blocking Execution Against the Shared Composition Root | Accepted | Supersedes ADR-020 | — |
| [ADR-050](ADR-050-cli-hub-concurrency-locking.md) | Cross-Process CLI–Hub Coordination via OS-Level Locks | Accepted | Supersedes ADR-020 | — |

## Maintenance

- Adding an ADR: use `docs/adr/TEMPLATE.md` (mandatory format, Constitution v2.0.0),
  append a row here in the same change.
- Superseding an ADR: always whole-ADR, never partial. Set the new ADR's `supersedes`
  frontmatter field, add the new ADR to the old ADR's `superseded_by` array and set its
  `status: superseded` in the same change, then update both rows here. Any still-valid
  aspect of the old ADR gets its own new single-aspect ADR.
- Extension is not invalidation: using more of a decided boundary or technology within
  its decided scope changes no status — at most an `Extends ADR-NNN` cross-reference.
  Each ADR's own Change Triggers section records what its author anticipated as
  extension vs. invalidation.
- Periodic review (Constitution Principle III "Review cadence"): externally observable
  ADRs at least every 90 days, purely internal-architecture ADRs at least every 365 days.
