# ADR Index

Central overview of all Architecture Decision Records, per Constitution Principle III
("ADR Status Maintenance"). This file MUST be updated in the same change as any ADR
whose status or existence changes — it is the single place to see which ADRs currently
govern the codebase without opening every file.

Status is one of exactly: `Accepted`, `Proposed`, `Deprecated`, `Superseded`. When an ADR
supersedes or amends another, both status headers carry the link (`Supersedes ADR-NNN` /
`Superseded by ADR-NNN`, or `Amends ADR-NNN` / `Amended by ADR-NNN`) — see the individual
ADR files for the authoritative header; this table mirrors it.

> **Known gap (as of 2026-08-11, constitution v1.10.0):** Several ADRs below already
> reach back and change earlier ADRs in substance without a corresponding status-header
> update — e.g. ADR-022's own "Superseded and amended decisions" table rewrites parts of
> ADR-009, ADR-002, ADR-007, ADR-012, ADR-019 and ADR-020, and ADR-013 carries a
> "Superseded (packaging / runtime-sharing aspects only)" section reaching into earlier
> ADRs — but none of the affected ADRs' own headers reflect it yet. Per the constitution's
> non-retroactivity clause this is not a violation of ADR-001 through ADR-023 themselves;
> retroactively adding the bidirectional links below is a separate, later cleanup pass,
> not performed here.

| ADR | Title | Status | Supersedes / Amends | Superseded by / Amended by |
| --- | --- | --- | --- | --- |
| [ADR-001](ADR-001-backend-frontend-tech-stack.md) | Backend and Frontend Technology Stack | Accepted | — | — |
| [ADR-002](ADR-002-ingest-agent-execution-model.md) | Ingest Agent Execution Model | Accepted | — | — |
| [ADR-003](ADR-003-domain-operational-state-persistence.md) | Domain vs. Operational State Persistence | Accepted | — | — |
| [ADR-004](ADR-004-credential-scoping.md) | Credential Scoping for the LLM API Key | Accepted | — | — |
| [ADR-005](ADR-005-observability-backend.md) | Observability Backend (Local and CI) | Accepted | — | — |
| [ADR-006](ADR-006-agent-tool-loop-guarded-boundary.md) | Agent Tool-Use Loop and Guarded Tool Boundary | Accepted | — | — |
| [ADR-007](ADR-007-agent-instruction-surface.md) | Agent Instruction Surface — Single System Prompt and Versioned Default User Prompt | Accepted | — | — |
| [ADR-008](ADR-008-agent-event-channel-run-supervision.md) | Agent Event Channel, Run Supervision, and Persistent Run Queue | Accepted | — | — |
| [ADR-009](ADR-009-runtime-path-configuration.md) | Explicit Runtime Path Configuration and Consolidated Data Directory | Accepted | — | — |
| [ADR-010](ADR-010-hexagonal-ports-adapter-namespaces.md) | Hexagonal Ports and Adapter Namespaces for External Systems | Accepted | — | — |
| [ADR-011](ADR-011-query-agent-shared-runtime-and-concurrency-model.md) | Shared Agent Runtime, Streaming, and Query Concurrency Model | Accepted | — | — |
| [ADR-012](ADR-012-eval-runner-recorded-replay.md) | Standalone Eval Runner and Recorded-Replay at the Model Port | Accepted | — | — |
| [ADR-013](ADR-013-unified-agent-platform-packaging-and-naming.md) | Unified Agent Platform Packaging and Agent-Artifact Naming Convention | Accepted | — | — |
| [ADR-014](ADR-014-query-conversation-records.md) | Query Conversation Records and Record-Sourced Follow-Up Context | Accepted | — | — |
| [ADR-015](ADR-015-query-write-scope-and-wiki-write-coordination.md) | Query Agent Write Scope and Cross-Process Wiki Write Coordination | Accepted | — | — |
| [ADR-016](ADR-016-lint-write-scope-frontmatter-only-enforcement.md) | Lint Write Scope — Structural Frontmatter-Only Enforcement | Accepted | — | — |
| [ADR-017](ADR-017-log-and-catalog-entry-format-enforcement.md) | Structural Format Enforcement for `log.md` and `index.md` Entries | Accepted | — | — |
| [ADR-018](ADR-018-remediation-action-authorization-and-execution.md) | Human-Authorized Remediation Action Execution | Accepted | — | — |
| [ADR-019](ADR-019-devcontainer-host-runtime-and-credential-access.md) | Devcontainer Host Container-Runtime and Credential Access | Accepted | — | — |
| [ADR-020](ADR-020-hub-cli-command-surface.md) | Hub CLI Command Surface — Framework, Dispatch, and In-Process Blocking Execution | Accepted | — | — |
| [ADR-021](ADR-021-test-tier-taxonomy-and-deterministic-wait-enforcement.md) | Backend Test Tier Taxonomy, Deterministic-Wait Convention, and Suite Concurrency Levers | Accepted | — | — |
| [ADR-022](ADR-022-minimal-directory-configuration-surface.md) | Minimal Directory Configuration Surface — Three Roots, Mandatory Configuration File, and Build-Distributed Agent Artifacts | Accepted | — | — |
| [ADR-023](ADR-023-hub-cli-default-command-and-root-help-routing.md) | Hub CLI Default Command and Root Help Routing | Accepted | — | — |

## Maintenance

- Adding an ADR: append a row here in the same change.
- Superseding/amending an ADR: update both the new and the old ADR's status header
  (Constitution Principle III), then update both rows here — the old row's Status
  changes to `Superseded`/kept `Accepted` with an `Amended by` note, and the chain
  columns on both rows are filled in.
- Periodic review (Constitution Principle III "Review cadence"): externally observable
  ADRs at least every 90 days, purely internal-architecture ADRs at least every 365 days.
