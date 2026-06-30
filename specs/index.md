# Grimoire — Specs Index

Tracks all feature specs and their current phase. Update this file whenever a spec advances.

**Phases:** `—` not started · `spec` specified · `plan` planned · `tasks` tasks generated · `wip` in progress · `done` complete

---

## MVP Core

| #   | Feature                                                   | Phase | ADRs | Notes                                               |
| --- | --------------------------------------------------------- | ----- | ---- | --------------------------------------------------- |
| 001 | Ingest Minimal — source in, wiki file + task artifact out | —     | TBD  | Vertical MVP core; all other features block on this |
| 002 | Lint Pass — trigger lint, findings as task files          | —     | —    | Needs wiki content from 001                         |
| 003 | Query Chat — chat answer + optional wiki page             | —     | —    | Needs wiki content from 001                         |

## Web UI

| #   | Feature                                             | Phase | ADRs | Notes             |
| --- | --------------------------------------------------- | ----- | ---- | ----------------- |
| 004 | Task History — browse all past task files in UI     | —     | —    | Blocks on 001–003 |
| 005 | Task Annotation — user can annotate/reply to a task | —     | —    | Blocks on 004     |

## Infrastructure & Cross-Cutting

| #   | Feature                                                                        | Phase | ADRs  | Notes                                               |
| --- | ------------------------------------------------------------------------------ | ----- | ----- | --------------------------------------------------- |
| 010 | Architecture Baseline — monorepo, .NET backend, Svelte frontend, ArchTests, CI | —     | §1 §4 | Prerequisite for all feature specs                  |
| 011 | Observability Baseline — OTel collector, exporter, local dev story             | —     | §8    | Required by constitution before any feature is Done |
| 012 | Security & Credential Boundary                                                 | —     | §6    | Auth/secrets — must not be added late               |

---

## Dependency Map

```
010 Architecture Baseline
 └── 001 Ingest Minimal
      ├── 002 Lint Pass
      ├── 003 Query Chat
      └── 004 Task History
           └── 005 Task Annotation

011 Observability Baseline  (gates DoD on all features)
012 Security Baseline       (cross-cutting, spec early)
```

---

## Open ADR Decisions

These problem domains (from `docs/decision-context-overview.md`) each need an ADR before implementation:

| §   | Domain                                       | Status   | File |
| --- | -------------------------------------------- | -------- | ---- |
| §1  | Backend & Frontend frameworks                | proposed | [ADR-001](../docs/adr/ADR-001-backend-frontend-frameworks.md) |
| §2  | Agent execution & orchestration model        | open     | — |
| §3  | External interface contracts (channels, API) | open     | — |
| §4  | Codebase & repository structure              | proposed | [ADR-004](../docs/adr/ADR-004-repository-and-code-structure.md) |
| §5  | State & persistence strategy                 | open     | — |
| §6  | Security & credential boundary               | open     | — |
| §7  | Agent guardrails & output safety             | open     | — |
| §8  | Observability strategy                       | open     | — |
| §9  | Simplicity budget & complexity governance    | open     | — |
| §10 | Open standards & protocol preference         | open     | — |
