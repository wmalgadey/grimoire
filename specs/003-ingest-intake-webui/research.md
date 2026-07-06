# Research: Ingest Intake Web UI

## Decision 0: Naming strategy for external interfaces

- Decision: Use a mixed model: `ingest` is the umbrella capability; public interfaces are named `ingest-submissions` and `ingest-lifecycle`; internal phase wording distinguishes `ingest submission` (pre-agent) from `ingest run` (agent-owned).
- Rationale: This keeps one canonical domain term (`ingest`) while still exposing function-oriented endpoints that map directly to what users do and observe.
- Alternatives considered:
  - Keep `intake` in all public paths (rejected: less explicit and inconsistent with latest clarification).
  - Use only `source-submission` naming everywhere (rejected: weakens alignment with ingest umbrella terminology in the spec).

## Decision 1: Use MarkItDown as central conversion entrypoint

- Decision: Use MarkItDown as the single conversion tool for all non-markdown sources and URL-derived content.
- Rationale: This keeps conversion behavior consistent across source kinds and avoids format-specific conversion drift in later tasks. It also aligns with the feature intent already captured in the specification and keeps conversion logic centralized in one adapter.
- Alternatives considered:
  - Dedicated per-format converters (rejected: higher maintenance and inconsistent outputs).
  - Postpone conversion to ingest-agent time (rejected: conflicts with clarified requirement that Hub intake owns fetch + conversion + persistence before trigger).

## Decision 2: Persist both original source artifact and normalized markdown artifact

- Decision: Intake stores two artifacts per accepted submission:
  1. Original artifact (raw bytes or fetched response body) under a dedicated originals path.
  2. Normalized markdown artifact under the existing raw markdown path used as ingest input.
- Rationale: This preserves provenance/auditability and allows future LLM-assisted troubleshooting or reprocessing to inspect the original content when markdown normalization loses context. It also satisfies the clarified URL behavior by making the persisted file the canonical downstream input.
- Alternatives considered:
  - Store only normalized markdown (rejected: loses provenance and limits diagnostics for conversion quality issues).
  - Store only URL bookmark and fetch later in agent (rejected: non-deterministic, weaker failure visibility, and conflicts with clarification).

## Decision 3: URL handling ownership

- Decision: Hub ingest-submission processing performs URL fetch during submission handling, then persists original + normalized artifacts and advances task lifecycle.
- Rationale: Early ownership gives deterministic failure semantics (`received/converting/failed`) and immediate user-visible status on the board.
- Alternatives considered:
  - Deferred fetch in agent runtime (rejected: shifts failures later, weaker UX transparency, and introduces volatility).

## Decision 4: Frontend component strategy

- Decision: Implement reusable UI primitives (e.g., SourceInputCard, SubmissionForm, StatusBadge, KanbanColumn, TaskCard) and route-level composition; avoid one-off markup patterns.
- Rationale: Reusability enforces consistent state rendering and lowers maintenance cost when more channels/views are added.
- Alternatives considered:
  - Page-local bespoke components (rejected: inconsistent behavior and styling drift).

## Decision 5: CSS framework choice for modern, visually strong UI (including Bulma evaluation)

- Decision: Use Tailwind CSS with a project design-token layer and a small curated component layer (Svelte components), not Bulma as primary framework.
- Rationale: Tailwind aligns well with Svelte component composition, enables fast creation of modern custom visual language, and supports tokenized theming without fighting opinionated global class structures.
- Alternatives considered:
  - Bulma:
    - Pros: easy class-based onboarding, clean defaults, lightweight mental model for basic layouts.
    - Cons: less flexible for distinctive design systems, weaker utility granularity for complex responsive states, and higher override pressure when building a bespoke visual identity.
    - Verdict: acceptable for simple admin UIs, but not preferred for this feature's requirement of modern, reusable, design-system-driven components.
  - Plain custom CSS only:
    - Pros: full control.
    - Cons: slower delivery, higher risk of inconsistency across components early on.

## Decision 6: Realtime board transport

- Decision: Use SignalR for board lifecycle updates and incremental card movement.
- Rationale: ADR-001 already standardizes SignalR for realtime backend/frontend communication and this feature depends on transparent status progression.
- Alternatives considered:
  - Polling-only board refresh (rejected: lower UX fidelity, unnecessary latency/load tradeoff).

## Decision 7: Testing approach for this feature

- Decision: Treat all success criteria as deterministic harness behavior and validate via integration/contract/e2e-style tests with controlled dispatcher and conversion doubles.
- Rationale: This feature mainly concerns submission pipeline orchestration and visibility, not wiki-content judgment quality.
- Alternatives considered:
  - Agent-eval-only validation (rejected: not sufficient to prove deterministic intake guarantees).

## Decision 8: ADR requirement for UI components and CSS frameworks

- Decision: No new ADR is required for this feature's UI component structure or CSS framework selection.
- Rationale: ADR-001 already fixes the frontend stack boundary (TypeScript + SvelteKit). Within that boundary, component patterns and CSS framework choice are implementation-level unless they introduce a new platform-wide architectural boundary.
- Alternatives considered:
  - Create a new ADR now for component library + CSS framework (rejected: adds governance overhead without a new cross-cutting architecture boundary).
  - Never use ADRs for UI topics (rejected: incorrect; ADR is required once a UI decision creates a project-wide architectural contract beyond a single feature).
