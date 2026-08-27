# ADR Index

Overview of all Architecture Decision Records, per Constitution Principle III ("ADR Status
Maintenance"): number, title, current status, and supersede chain. Updated in the same
change as any ADR whose status or existence changes. Pre-v2.0.0 `Amends` entries are
historical record (Governance non-retroactivity), not a pattern for new ADRs.

| ADR | Title | Status | Supersedes / Amends | Superseded by / Amended by |
| --- | --- | --- | --- | --- |
| [ADR-001](ADR-001-backend-frontend-tech-stack.md) | Backend and Frontend Technology Stack | Accepted | — | — |
| [ADR-002](ADR-002-ingest-agent-execution-model.md) | Ingest Agent Execution Model | Superseded | — | Superseded by ADR-036; formerly amended by ADR-008, ADR-009, ADR-022, ADR-025 |
| [ADR-003](ADR-003-domain-operational-state-persistence.md) | Domain vs. Operational State Persistence | Accepted | — | Superseded in part by ADR-009 (naming detail only) |
| [ADR-004](ADR-004-credential-scoping.md) | Credential Scoping for the LLM API Key | Accepted | — | Amended by ADR-009 |
| [ADR-005](ADR-005-observability-backend.md) | Observability Backend (Local and CI) | Accepted | — | — |
| [ADR-006](ADR-006-agent-tool-loop-guarded-boundary.md) | Agent Tool-Use Loop and Guarded Tool Boundary | Accepted | — | Amended by ADR-009, ADR-030, ADR-031 |
| [ADR-007](ADR-007-agent-instruction-surface.md) | Agent Instruction Surface — Single System Prompt and Versioned Default User Prompt | Accepted | — | Amended by ADR-009, ADR-022, ADR-029 |
| [ADR-008](ADR-008-agent-event-channel-run-supervision.md) | Agent Event Channel, Run Supervision, and Persistent Run Queue | Superseded | Amends ADR-002 | Superseded by ADR-037, ADR-038, ADR-039; formerly amended by ADR-009, ADR-025 |
| [ADR-009](ADR-009-runtime-path-configuration.md) | Explicit Runtime Path Configuration and Consolidated Data Directory | Superseded | Supersedes ADR-003 (in part); amends ADR-002, ADR-004, ADR-006, ADR-007, ADR-008 | Superseded by ADR-040; previously superseded in part by ADR-022 |
| [ADR-010](ADR-010-hexagonal-ports-adapter-namespaces.md) | Hexagonal Ports and Adapter Namespaces for External Systems | Accepted | — | Amended by ADR-011 (`IModelClient` port row only) |
| [ADR-011](ADR-011-query-agent-shared-runtime-and-concurrency-model.md) | Shared Agent Runtime, Streaming, and Query Concurrency Model | Superseded | Amends ADR-010 | Superseded by ADR-044, ADR-045, ADR-046, ADR-047; aspects previously carved out to ADR-013, ADR-014, ADR-015, ADR-030 |
| [ADR-012](ADR-012-eval-runner-recorded-replay.md) | Standalone Eval Runner and Recorded-Replay at the Model Port | Accepted | — | Amended by ADR-022 |
| [ADR-013](ADR-013-unified-agent-platform-packaging-and-naming.md) | Unified Agent Platform Packaging and Agent-Artifact Naming Convention | Accepted | Amends ADR-011 | — |
| [ADR-014](ADR-014-query-conversation-records.md) | Query Conversation Records and Record-Sourced Follow-Up Context | Accepted | Supersedes ADR-011 (persistence section only) | — |
| [ADR-015](ADR-015-query-write-scope-and-wiki-write-coordination.md) | Query Agent Write Scope and Cross-Process Wiki Write Coordination | Accepted | Supersedes ADR-011 (write-free framing only) | — |
| [ADR-016](ADR-016-lint-write-scope-frontmatter-only-enforcement.md) | Lint Write Scope — Structural Frontmatter-Only Enforcement | Superseded | Extends ADR-015 | Superseded by ADR-031 |
| [ADR-017](ADR-017-log-and-catalog-entry-format-enforcement.md) | Structural Format Enforcement for `log.md` and `index.md` Entries | Deprecated | Extends ADR-006, ADR-015, ADR-016 | Deprecated without replacement (see frontmatter `reason`) |
| [ADR-018](ADR-018-remediation-action-authorization-and-execution.md) | Human-Authorized Remediation Action Execution | Accepted | — | Amended by ADR-031 |
| [ADR-019](ADR-019-devcontainer-host-runtime-and-credential-access.md) | Devcontainer Host Container-Runtime and Credential Access | Accepted | — | Amended by ADR-022 |
| [ADR-020](ADR-020-hub-cli-command-surface.md) | Hub CLI Command Surface — Framework, Dispatch, and In-Process Blocking Execution | Superseded | — | Superseded by ADR-048, ADR-049, ADR-050; formerly amended by ADR-022, ADR-023 |
| [ADR-021](ADR-021-test-tier-taxonomy-and-deterministic-wait-enforcement.md) | Backend Test Tier Taxonomy, Deterministic-Wait Convention, and Suite Concurrency Levers | Accepted | — | Amended by ADR-033 |
| [ADR-022](ADR-022-minimal-directory-configuration-surface.md) | Minimal Directory Configuration Surface — Three Roots, Mandatory Configuration File, and Build-Distributed Agent Artifacts | Superseded | Amends ADR-002, ADR-007, ADR-012, ADR-019, ADR-020; supersedes ADR-009 (in part) | Superseded by ADR-041, ADR-042, ADR-043; formerly amended by ADR-024, ADR-032 |
| [ADR-023](ADR-023-hub-cli-default-command-and-root-help-routing.md) | Hub CLI Default Command and Root Help Routing | Accepted | Amends ADR-020 | — |
| [ADR-024](ADR-024-memory-directory-root.md) | Memory Directory — A Fourth Independent Root for Agent Process Bookkeeping | Accepted | Amends ADR-022 | Amended by ADR-032 |
| [ADR-025](ADR-025-ingest-task-lifecycle-reentry.md) | Ingest Task Lifecycle Re-Entry — Liveness Reactivation, Manual Restart, and Status History | Accepted | Amends ADR-002, ADR-008 | — |
| [ADR-026](ADR-026-hub-api-error-contract-and-frontend-error-presentation.md) | Hub API Error Response Contract and Shared Frontend Error Presentation | Accepted | Extends ADR-020, ADR-013 | — |
| [ADR-027](ADR-027-gitversion-github-flow.md) | Version Numbers Computed by GitVersion, Branching by GitHub Flow | Accepted | — | — |
| [ADR-028](ADR-028-agent-owned-activity-log-prepend-ordering.md) | Agent-Owned Activity Log — Prepend-Only Ordering and Removal of Harness Authorship | Superseded | Amends ADR-017 | Superseded by ADR-035 |
| [ADR-029](ADR-029-harness-operator-turn-delimiter.md) | Harness Operator Turns Are Delimited Inside the User Channel | Accepted | Amends ADR-007 | — |
| [ADR-030](ADR-030-guarded-retrieval-tool-surface.md) | Guarded Retrieval Tools — Search, Ranged Read, and Read-Only Batch | Accepted | Amends ADR-006, ADR-011 | — |
| [ADR-031](ADR-031-lint-full-wiki-write-scope.md) | Lint Holds Full Authority Over Wiki Content, in Both Modes | Accepted | Supersedes ADR-016; amends ADR-017, ADR-018, ADR-006 | — |
| [ADR-032](ADR-032-behavioral-enforcement-for-path-surface-invariants.md) | Behavioral Enforcement for Feature-Scoped Path-Surface Invariants | Accepted | Amends ADR-022, ADR-024 | — |
| [ADR-033](ADR-033-sloweval-replay-class-set-reduction.md) | SlowEval Replay Class Set Reduced by the Lower-Stakes Eval Removal | Accepted | Amends ADR-021 | — |
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

- Adding an ADR: use `docs/adr/TEMPLATE.md` (mandatory format and frontmatter contract),
  append a row here in the same change.
- Superseding an ADR: always whole-ADR, never partial (Constitution v2.0.0). Set the new
  ADR's `supersedes`, add it to the old ADR's `superseded_by` with a `reason` and
  `status: superseded` in the same change, then update both rows here.
- Periodic review (Constitution Principle III "Review cadence"): externally observable
  ADRs at least every 90 days, purely internal-architecture ADRs at least every 365 days.
