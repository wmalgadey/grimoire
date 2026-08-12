# ADR Index

Central overview of all Architecture Decision Records, per Constitution Principle III
("ADR Status Maintenance"). This file MUST be updated in the same change as any ADR
whose status or existence changes — it is the single place to see which ADRs currently
govern the codebase without opening every file.

Status is one of exactly: `Accepted`, `Proposed`, `Deprecated`, `Superseded` (case-
insensitive — ADR files' YAML frontmatter uses lowercase, e.g. `status: accepted`; this
table uses Title Case for readability, not a distinct value). When an ADR supersedes or
amends another, both status headers carry the link (`Supersedes ADR-NNN` /
`Superseded by ADR-NNN`, or `Amends ADR-NNN` / `Amended by ADR-NNN`) — see the individual
ADR files for the authoritative header; this table mirrors it. None of the relationships
below fully retires an ADR's core decision (each is scoped — "in part", "this section
only", a single table row) — every ADR therefore stays `Accepted`; the chain columns
qualify what still applies.

`Extends` relationships (ADR-016 on ADR-015; ADR-017 on ADR-006/ADR-015/ADR-016) are
listed for context but are not supersede/amend links — their own text states they
supersede no part of what they extend, so Constitution Principle III's bidirectional
linking rule does not apply to them.

| ADR | Title | Status | Supersedes / Amends | Superseded by / Amended by |
| --- | --- | --- | --- | --- |
| [ADR-001](ADR-001-backend-frontend-tech-stack.md) | Backend and Frontend Technology Stack | Accepted | — | — |
| [ADR-002](ADR-002-ingest-agent-execution-model.md) | Ingest Agent Execution Model | Accepted | — | Amended by ADR-008, ADR-009, ADR-022 |
| [ADR-003](ADR-003-domain-operational-state-persistence.md) | Domain vs. Operational State Persistence | Accepted | — | Superseded in part by ADR-009 |
| [ADR-004](ADR-004-credential-scoping.md) | Credential Scoping for the LLM API Key | Accepted | — | Amended by ADR-009 |
| [ADR-005](ADR-005-observability-backend.md) | Observability Backend (Local and CI) | Accepted | — | — |
| [ADR-006](ADR-006-agent-tool-loop-guarded-boundary.md) | Agent Tool-Use Loop and Guarded Tool Boundary | Accepted | — | Amended by ADR-009 |
| [ADR-007](ADR-007-agent-instruction-surface.md) | Agent Instruction Surface — Single System Prompt and Versioned Default User Prompt | Accepted | — | Amended by ADR-009, ADR-022 |
| [ADR-008](ADR-008-agent-event-channel-run-supervision.md) | Agent Event Channel, Run Supervision, and Persistent Run Queue | Accepted | Amends ADR-002 | Amended by ADR-009 |
| [ADR-009](ADR-009-runtime-path-configuration.md) | Explicit Runtime Path Configuration and Consolidated Data Directory | Accepted | Supersedes ADR-003 (in part); amends ADR-002, ADR-004, ADR-006, ADR-007, ADR-008 | Superseded in part by ADR-022 |
| [ADR-010](ADR-010-hexagonal-ports-adapter-namespaces.md) | Hexagonal Ports and Adapter Namespaces for External Systems | Accepted | — | Amended by ADR-011 (`IModelClient` port row only) |
| [ADR-011](ADR-011-query-agent-shared-runtime-and-concurrency-model.md) | Shared Agent Runtime, Streaming, and Query Concurrency Model | Accepted | Amends ADR-010 | Amended by ADR-013 (packaging only); superseded in part by ADR-014 ("Persistence and conversation context" section), ADR-015 ("Query is structurally write-free" framing) |
| [ADR-012](ADR-012-eval-runner-recorded-replay.md) | Standalone Eval Runner and Recorded-Replay at the Model Port | Accepted | — | Amended by ADR-022 |
| [ADR-013](ADR-013-unified-agent-platform-packaging-and-naming.md) | Unified Agent Platform Packaging and Agent-Artifact Naming Convention | Accepted | Amends ADR-011 (packaging / runtime-sharing aspects only) | — |
| [ADR-014](ADR-014-query-conversation-records.md) | Query Conversation Records and Record-Sourced Follow-Up Context | Accepted | Supersedes ADR-011 ("Persistence and conversation context" section only) | — |
| [ADR-015](ADR-015-query-write-scope-and-wiki-write-coordination.md) | Query Agent Write Scope and Cross-Process Wiki Write Coordination | Accepted | Supersedes ADR-011 ("Query is structurally write-free" framing only) | — |
| [ADR-016](ADR-016-lint-write-scope-frontmatter-only-enforcement.md) | Lint Write Scope — Structural Frontmatter-Only Enforcement | Accepted | Extends ADR-015 (no supersession) | — |
| [ADR-017](ADR-017-log-and-catalog-entry-format-enforcement.md) | Structural Format Enforcement for `log.md` and `index.md` Entries | Accepted | Extends ADR-006, ADR-015, ADR-016 (no supersession) | — |
| [ADR-018](ADR-018-remediation-action-authorization-and-execution.md) | Human-Authorized Remediation Action Execution | Accepted | — | — |
| [ADR-019](ADR-019-devcontainer-host-runtime-and-credential-access.md) | Devcontainer Host Container-Runtime and Credential Access | Accepted | — | Amended by ADR-022 |
| [ADR-020](ADR-020-hub-cli-command-surface.md) | Hub CLI Command Surface — Framework, Dispatch, and In-Process Blocking Execution | Accepted | — | Amended by ADR-022, ADR-023 |
| [ADR-021](ADR-021-test-tier-taxonomy-and-deterministic-wait-enforcement.md) | Backend Test Tier Taxonomy, Deterministic-Wait Convention, and Suite Concurrency Levers | Accepted | — | — |
| [ADR-022](ADR-022-minimal-directory-configuration-surface.md) | Minimal Directory Configuration Surface — Three Roots, Mandatory Configuration File, and Build-Distributed Agent Artifacts | Accepted | Amends ADR-002, ADR-007, ADR-012, ADR-019, ADR-020; supersedes ADR-009 (in part) | Amended by ADR-024 |
| [ADR-023](ADR-023-hub-cli-default-command-and-root-help-routing.md) | Hub CLI Default Command and Root Help Routing | Accepted | Amends ADR-020 | — |
| [ADR-024](ADR-024-memory-directory-root.md) | Memory Directory — A Fourth Independent Root for Agent Process Bookkeeping | Accepted | Amends ADR-022 (R1 switch cap, sub-path anchoring) | — |

## Maintenance

- Adding an ADR: append a row here in the same change.
- Superseding/amending an ADR: update both the new and the old ADR's status header
  (Constitution Principle III), then update both rows here — the old row's Status
  changes to `Superseded`/kept `Accepted` with an `Amended by` note, and the chain
  columns on both rows are filled in.
- Periodic review (Constitution Principle III "Review cadence"): externally observable
  ADRs at least every 90 days, purely internal-architecture ADRs at least every 365 days.
