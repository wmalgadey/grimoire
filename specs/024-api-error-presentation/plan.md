# Implementation Plan: Readable API Error Presentation

**Branch**: `claude/next-feature-issue-afw3vx` (spec directory `024-api-error-presentation`) | **Date**: 2026-08-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/024-api-error-presentation/spec.md`

## Summary

The Hub answers failures in two ad-hoc shapes — `{ message }` prose and `{ reason }` machine codes,
sometimes only the latter — and has no exception middleware at all, so an unhandled endpoint
exception produces a bare 500 with an empty body. The browser compensates with three duplicated
`snake_case`→prose tables and two mutually inconsistent parse helpers, then renders the result as one
line of small text with no category, no technical detail, and no retry.

This plan replaces that with one contract and one presentation. A new cross-agent Hub namespace,
`Grimoire.Hub.ApiErrors`, owns a catalogue of failure definitions and the only sanctioned way to
produce an error `IResult`; every endpoint routes through it, and an exception handler brings the
unhandled path into the same shape. The browser gets one derivation module and one hand-built Svelte
alert component; the three lookup tables and both parse helpers are deleted.

Two placement constraints shape the design and are non-negotiable:

- **The envelope is composed at the HTTP endpoint boundary, above the shared coordinator layer.**
  ADR-020 requires CLI and HTTP to drive the *same* coordinator methods, with the CLI's own failure
  contract being exit codes plus a stdout/stderr split. Composing the envelope inside coordinators
  would push HTTP response shaping into the CLI path.
- **The correlation identifier is read from the ambient trace context inside our own helper**, and
  verified through the production telemetry registration. Constitution Principle IV's
  production-wiring rule exists because feature 003 shipped green trace tests while exporting
  nothing on this exact request path.

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript / Svelte 5 (runes) on SvelteKit 2 (frontend)

**Primary Dependencies**: ASP.NET Core Minimal APIs, OpenTelemetry .NET SDK (OTLP), SignalR; Vite 8,
Vitest 4 (browser mode via Playwright/Chromium), Tailwind 4. No new package on either side —
`ProblemDetails` and `IExceptionHandler` ship with ASP.NET Core, and ADR-001's recorded mitigation
("the UI is deliberately built from a small set of central, reusable components rather than a large
surface of off-the-shelf widgets") rules out pulling in a third-party toast/alert library.

**Storage**: None. This feature persists nothing.

**Testing**: xUnit — `Grimoire.IntegrationTests` (Integration tier, untagged per ADR-021) for the
envelope and observability contracts, `Grimoire.ArchTests` for the Boundary Rule; Vitest for the
frontend module (server project) and components (client/browser project).

**Target Platform**: Linux/macOS developer machine; Hub on :5255, frontend dev server proxying
`/api` and `/hubs`.

**Project Type**: Web application — existing `backend/` + `frontend/` split.

**Performance Goals**: None specific. Error composition is off the hot path; the added work per
failed request is one catalogue lookup and one activity-id read.

**Constraints**: No fixed waits in deterministic tests (ADR-021). No Hub→agent assembly reference
(ADR-002/ADR-022). No new external system, therefore no new port (Principle I).

**Scale/Scope**: ~20 error call sites across five Hub endpoint namespaces; ~11 frontend surfaces
that render an error; 4 frontend client modules; 3 lookup tables and 2 parse helpers deleted.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see the second column.*

| Principle | Requirement as it applies here | Verdict |
|---|---|---|
| **I — Domain architecture & hexagonal boundaries** | No new external system → no new port required (Principle I's trigger is "a dependency on a new external system"). No infrastructure package enters a domain or orchestration namespace. A new Hub namespace must be registered in ADR-013's Hub ownership map. | **Pass** — registration is task T004. |
| **II — Pragmatic testing** | Integration tests against a real Hub over real HTTP are primary. No mocking framework. Doubles only as hand-rolled fakes on existing ports — this feature adds none. Success criteria are all deterministic; the spec states why there are no agent-judgment thresholds. | **Pass** |
| **II — Test what we own** | Every assertion must be able to fail from a change to Grimoire's own source. We assert *our* envelope members, *our* catalogue completeness, *our* category derivation — never that ASP.NET serializes `ProblemDetails`, that the exception-handler pipeline invokes a registered handler, or that Vitest renders a component. | **Pass** — see Test Strategy. |
| **III — ADR-driven & test-enforced** | This feature introduces a Hub-wide cross-cutting concern no ADR covers. A new ADR is mandatory and must reach Accepted before `/speckit-tasks`. Each rule it names must be tagged Boundary Rule or Feature-Scoped Invariant. | **Pass** — [ADR-026](../../docs/adr/ADR-026-hub-api-error-contract-and-frontend-error-presentation.md) drafted as part of this plan. |
| **IV — Behavioral & observable** | `## Observability` below is mandatory, and every row must map to implementation + deterministic test + CI tasks. Contract tests must exercise the production composition root. | **Pass** |
| **V — Agentic core & deterministic harness** | Error copy is operator-facing harness text, not wiki content. No instruction file is touched. No wiki-content judgment is added to backend code. | **Pass** — no agentic surface. |

**Post-design re-check**: unchanged. The design adds no port, no persisted state, no infrastructure
package, and no instruction-file dependency. The one item that moved during design is the FR-015
redaction path — see "Redaction" under Architectural Constraints.

## Architectural Constraints & ADRs

*GATE: All 25 ADRs in `docs/adr/` were read for this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend and Frontend Technology Stack | Fixes ASP.NET Core Minimal APIs + SvelteKit. Its stated rationale — "a small set of central, reusable UI components governed by shared CSS, rather than many one-off, inconsistently styled views" — is exactly what FR-010/SC-005 asks for, and forbids satisfying FR-009 with a third-party widget library. |
| ADR-002 | Ingest Agent Execution Model | The task artifact is agent-owned; FR-012's "what the harness records MUST NOT change" is ADR-002 compliance. Its Hub→agent reference ban (enforced by `HubAgentDispatchBoundaryRuleTests`; `Grimoire.Hub.csproj` references only `Grimoire.Domain`) means the Hub cannot reuse `ErrorSanitizer` — see Redaction below. |
| ADR-004 | Credential Scoping | The basis of FR-015/SC-008. A display path that echoed credential-shaped text would defeat its "one file, one injection point, one consumer" auditability claim. |
| ADR-005 | Observability Backend | OTLP exporters; CI verifies instrumentation with an in-memory exporter wired into integration tests. This feature declares its own spans/metrics/log events on top and re-decides nothing. |
| ADR-010 | Hexagonal Ports and Adapter Namespaces | No new port: the port inventory covers external systems, and an error envelope is not one. Containment rules C1–C5 are untouched (C3 governs *outbound* HTTP from the Hub, not inbound response shaping). Its exemption for composition-root/telemetry wiring covers registering the exception handler in `HubHostComposition`. |
| ADR-011 | Shared Agent Runtime and Query Concurrency | Origin of `query_concurrency_limit_reached`, preserved verbatim. Its structurally separate SignalR lifecycle channel means FR-010's "every UI surface" covers HTTP request failures only — `ConnectionStatusIndicator` keeps its own presentation. |
| ADR-013 | Packaging and Agent-Artifact Naming (N1) | New Hub test classes span ingest, query, lint and remediation endpoints → cross-agent → **unprefixed** names (precedent: `HubRequestTracingTests`). The new `Grimoire.Hub.ApiErrors` namespace must be added to the Hub namespace-ownership map as Cross-agent in `docs/conventions/agent-artifact-naming.md` **and** mirrored in `AgentArtifactNamingRuleTests` in the same change, or the build fails. Frozen agent OTel identities are untouched. |
| ADR-014 | Query Conversation Records | Pins `conversation_record_unreadable` as an ADR-level identifier — it must survive verbatim as a `code`. Also the sharpest semantic edge: it is a 500 that retrying will deterministically re-fail until a new conversation is started. See "Retry semantics" below. |
| ADR-018 | Remediation Action Authorization | Its state machine requires that "the loser is rejected and surfaced to the caller (the board shows the actual outcome)". Each conflict outcome therefore needs its **own** catalogue entry — collapsing them into one generic "request declined" message would contradict this ADR. |
| ADR-020 | Hub CLI Command Surface | **The binding placement constraint.** The CLI's failure contract is exit codes plus a stdout/stderr split, and CLI and HTTP must drive the same coordinator methods with behavioral parity. The envelope is therefore composed at the HTTP endpoint boundary, never inside a coordinator or transition service. |
| ADR-021 | Test Tier Taxonomy and Deterministic Waits | Backend tests belong in `Grimoire.IntegrationTests`, untagged. `PollAsync` is the only sanctioned wait; `Task.Delay`/`Thread.Sleep` in this tier fails `DeterministicTierNoFixedWaitRuleTests` — directly relevant, because the "system unreachable" case invites a sleep. Observability tests attaching process-wide listeners join the existing serialized collection. |
| ADR-023 | CLI Default Command and Root Help Routing | The web host boots through the CLI default command, so the exception handler must be registered in `HubHostComposition` (where `AddHubTelemetry` already is), not in `Program.cs`, which is now a one-line pass-through. |
| ADR-025 | Ingest Task Lifecycle Re-Entry | Owns `POST /api/ingest-submissions/{taskId}/restart` and its two distinct decline reasons (not `failed`; normalized source missing). Each gets its own catalogue entry per ADR-018's rule above. Its "a task may re-enter `running` repeatedly" consequence means a surface's error state keys on current stage, not on transition count (FR-011). |

**Not constraining, checked and excluded**: ADR-003 (nothing persisted), ADR-006/015/016/017 (their
ten denial-reason strings are *agent-facing* tool errors delivered into the model conversation as
`is_error` results — explicitly outside FR-016's scope, see below), ADR-007/012 (no instruction file
touched, so no eval re-capture gate), ADR-009/019/022/024 (no path, switch, or configuration key
added).

**FR-016 scope statement (required to avoid a false audit)**: "every distinct failure the API can
return" means the **HTTP API surface only**. The guarded-boundary denial vocabulary fixed by
ADR-015/016/017 — `create_only_target_exists`, `write_conflict_stale_read`,
`write_coordination_timeout`, `frontmatter_only_target_missing`,
`frontmatter_only_malformed_document`, `frontmatter_only_body_changed`, `log_entry_not_appended`,
`log_entry_malformed_heading`, `log_entry_missing_paragraph`, `catalog_entry_malformed` — is
addressed to the model, not the operator, and is out of scope. It reaches a human only as
`deniedActions` on a task artifact, which FR-012 leaves unchanged.

**Redaction (FR-015)**: the Hub needs no redactor, and must not acquire one. Three facts close the
requirement without a new dependency: (1) catalogue text is authored by us and contains no runtime
data; (2) an unhandled exception's message reaches the log, never the response body — the response
carries the generic `internal_error` entry; (3) recorded agent failure text was already sanitized by
`ErrorSanitizer` inside the agent process before it ever reached the Hub. The `bodyExcerpt` in the
technical detail is browser-side and bounded, taken from a response the Hub produced. No
Hub→`Grimoire.AgentRuntime` reference is introduced.

**Retry semantics, two honest edge cases**: `conversation_record_unreadable` (ADR-014) and the
restart decline "normalized source missing" (ADR-025) are failures no retry and no user action can
resolve. The first is a 500 and would be offered a retry by category alone; the second is a 4xx
dead end. Both are handled by the catalogue's `detail` naming the actual recovery ("start a new
conversation", "re-submit the source") rather than by adding a fifth category or a per-code retry
flag — keeping FR-007/FR-008/SC-004's four-category model intact while still telling the user what
to do.

**New ADR required?**: **Yes** — [`docs/adr/ADR-026-hub-api-error-contract-and-frontend-error-presentation.md`](../../docs/adr/ADR-026-hub-api-error-contract-and-frontend-error-presentation.md),
drafted as part of this plan output. It must reach Accepted before `/speckit-tasks`, and its row must
be appended to `docs/adr/index.md` in the same change.

## Agentic Boundary (Constitution Principle V)

**No agentic surface — harness-only feature.** Every capability introduced is harness: request
dispatch, error-response composition, observability, and presentation. No wiki-content judgment is
added to backend code, no instruction file is read or written, and no agent behaviour changes. The
one adjacency is FR-012, which *displays* text an agent's failure produced without altering what was
recorded.

## Test Strategy

*Every success criterion maps to its primary verification method.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| **SC-001** — every declining/failing response carries the envelope with all fields | Deterministic guarantee | Hermetic integration test over real HTTP (`Grimoire.IntegrationTests`) | Real Hub host via `IngestApiHost`/sibling hosts, real SQLite + temp filesystem via `IngestSubmissionPipelineFixture`, fake agent launcher (existing port fake) | One request per documented failure across ingest, query, lint, remediation, restart | Asserts media type `application/problem+json` and all four required members. Product-owned: only our own composition can turn it red. |
| **SC-002** — readable message is primary; no code/status/JSON in that position | Deterministic guarantee | Frontend component tests (Vitest browser project) + integration assertion that `detail` is prose | None — real component render | Envelope fixtures per category | Component test asserts the primary region's text equals `detail`; a separate assertion pins that no catalogue `detail` contains its own `code`. |
| **SC-003** — technical detail exposes status, code, traceId | Deterministic guarantee | Frontend component test | None | Envelope fixture with and without `traceId` | Asserts the disclosure content, and that the fields are absent from the primary region when closed. |
| **SC-004** — four categories distinguishable; retry on exactly the two retryable ones | Deterministic guarantee | Frontend module test (derivation) + component test (rendering) | A thrown fetch rejection and hand-built `Response` objects — no framework doubles | One input per row of data-model.md's derivation table | Derivation is pure and state-based: input → `PresentedError`. |
| **SC-005** — every surface renders through the shared presentation | Deterministic guarantee (Feature-Scoped Invariant) | Classicist behavioural test per surface (Vitest) | None | A failing request per surface | Asserts the *concern* — each surface shows the shared presentation's structure — never a count of components. The three lookup tables and both parse helpers are deleted, so there is nothing to regress to. |
| **SC-006** — every code resolves to a defined message; unknown codes still readable | Deterministic guarantee | Integration test over the real catalogue + real HTTP | None | The catalogue itself | Two assertions: every entry has non-empty title/detail; every `code` an endpoint emits exists in the catalogue. Plus an unknown-code lookup returning the generic entry. |
| **SC-007** — primary message length-bounded, full text recoverable | Deterministic guarantee | Frontend module + component test | None | An over-length `detail` and an over-length recorded `failureReason` | State-based: bounded `message`, `fullMessage` carries the original. |
| **SC-008** — redaction preserved on every display path | Deterministic guarantee | Integration test (response body never carries exception text) + frontend test (recorded `[REDACTED]` text survives presentation) | Real Hub; an endpoint made to throw | A recorded failure string containing `[REDACTED]` | Asserts the unhandled-exception response body contains the generic detail and **not** the exception message. |
| **Observability rows** (below) | Deterministic guarantee | Integration tests through the production composition root | Real `AddHubTelemetry` with in-memory exporters attached via its `configureTracing`/`configureMetrics` seams | — | Never a hand-registered `ActivitySource` or an always-on listener (Principle IV). |

**Explicitly not tested** (Principle II, "Test what we own"): that ASP.NET Core serializes
`ProblemDetails`; that a registered `IExceptionHandler` is invoked by the pipeline; that Svelte
renders reactive state; that `fetch` rejects on a connection failure. Where the framework wiring is
load-bearing, it is covered by exactly one intent-named wire-up test —
`ApiErrorExceptionHandler_IsRegistered_AndReachesOurHandler` — proving our registration is present
and reaches our code, not that the framework works.

**Doubles**: none added. The existing fake agent launcher (a hand-rolled fake on the
`IAgentProcessLauncher` port) is reused. No mocking framework is referenced.

**Deterministic waits**: the "system unreachable" case is exercised by a fetch rejection in the
frontend module test — no timer, no sleep. Backend polling uses `PollAsync` (ADR-021).

## Observability

*Code without this instrumentation fails the Definition of Done.*

Naming follows the Hub's existing convention: `hub.*` for Hub-infrastructure facts, counters
suffixed `_total`, a `reason`-style label set enumerated in the ADR (ADR-024's precedent).

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `hub.api_errors_total` | Counter | Error responses returned by the Hub, one increment per envelope produced | `code` (catalogue code), `status` (HTTP status) |

Registered on the existing `Grimoire.Hub` meter (`HubMetrics`), so it reaches an observer through
the production `AddMeter("Grimoire.Hub")` registration.

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `api.error.declined` | WARN | An envelope with a 4xx status is returned | `code`, `status`, `path`, `trace_id` |
| `api.error.faulted` | ERROR | An envelope with a 5xx status is returned, including the unhandled-exception path | `code`, `status`, `path`, `trace_id`, `failure_reason` |

`failure_reason` carries the exception's message on the fault path — **in the log only**; it never
enters the response body (FR-015). Events are emitted through a new `ApiErrorLogEvents` static class
holding `EventId(int, "<stable.name>")`, with snake_case message-template placeholders matching the
field names, exactly as the Hub's existing `*LogEvents` classes do.

**Derivation rule (MANDATORY)**: each row maps to (1) an implementation task emitting the event with
its stable name and mandatory fields, (2) a deterministic integration test validating name, level
and every mandatory field, and (3) a CI task confirming those tests run in the standard PR pipeline.
See tasks T012–T014 and T031.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `api.error.declined` | the ASP.NET Core request activity | `signal_type=log`, `event_name=api.error.declined`, `level=Warning`, `code`, `status` |
| `api.error.faulted` | the ASP.NET Core request activity | `signal_type=log`, `event_name=api.error.faulted`, `level=Error`, `code`, `status` |

These are the Hub's established **log-shaped spans** (the `StartLogEventSpan` idiom used by every
existing `*LogEvents` class, and by `WikiLogEvents`), not a separate operation span — an error
response is one event, and inventing a second `hub.api_error.compose` span would emit two spans per
failure for no added signal.

The parent relationship is the load-bearing assertion, not an incidental one: this is the same
request path where feature 003 exported nothing because every span was parented to an unsampled
activity. The trace test asserts the span is **exported under the production registration** and that
its parent is the request activity — which is also what proves the `traceId` in the response body is
the one an operator will find.

**Derivation rule (MANDATORY)**: each row maps to (1) an implementation task creating the span with
the declared parent linkage and attributes, (2) a deterministic integration test validating span
name, parent/child linkage and correlation attributes, and (3) a CI task. See tasks T015–T016 and
T031.

**Correlation**: `trace_id` is the shared identifier joining the metric, both log events, both spans,
and the `traceId` member of the response body. It is read from `Activity.Current` inside our own
helper, so the joining fact is product-owned and testable.

## Project Structure

### Documentation (this feature)

```text
specs/024-api-error-presentation/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── api-error-envelope.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
backend/
├── src/Grimoire.Hub/
│   ├── ApiErrors/                          # NEW cross-agent namespace Grimoire.Hub.ApiErrors
│   │   ├── ApiErrorDefinition.cs           #   one catalogue entry
│   │   ├── ApiErrorCatalogue.cs            #   the collection + Resolve/fallback
│   │   ├── ApiErrorResults.cs              #   the ONLY producer of error IResults
│   │   ├── ApiErrorExceptionHandler.cs     #   IExceptionHandler -> same envelope
│   │   └── ApiErrorLogEvents.cs            #   api.error.declined / api.error.faulted
│   ├── HubMetrics.cs                       # + hub.api_errors_total
│   ├── HubHostComposition.cs               # + exception-handler registration
│   ├── IngestSubmission/                   # call sites migrated to ApiErrorResults
│   ├── QuerySubmission/                    #   "
│   ├── QueryConversations/                 #   "
│   ├── LintDispatch/                       #   "
│   └── RemediationTasks/                   #   "
└── tests/
    ├── Grimoire.ArchTests/
    │   └── HubApiErrorResultContainmentRuleTests.cs   # Boundary Rule BR1 (Phase 0)
    └── Grimoire.IntegrationTests/
        ├── HubApiErrorEnvelopeTests.cs                # SC-001, SC-006, SC-008
        └── HubApiErrorObservabilityTests.cs           # metric + both log events + both spans

frontend/
└── src/lib/
    ├── services/
    │   ├── apiError.ts                     # NEW: derivation (replaces httpErrorMessage.ts)
    │   ├── apiError.test.ts                # NEW: SC-004, SC-007
    │   ├── httpErrorMessage.ts             # DELETED
    │   ├── lintApi.ts                      # REASON_MESSAGES deleted, uses apiError
    │   ├── remediationApi.ts               #   "
    │   ├── querySubmissionApi.ts           #   "
    │   └── ingestSubmissionsApi.ts         #   "
    └── components/
        ├── ApiErrorAlert.svelte            # NEW: the one presentation
        └── ApiErrorAlert.svelte.test.ts    # NEW: SC-002, SC-003, SC-004, SC-007
```

**Structure Decision**: the existing `backend/` + `frontend/` web-application split is kept. The one
new backend namespace, `Grimoire.Hub.ApiErrors`, is a cross-agent Hub namespace in the sense ADR-013's
ownership map defines — it serves the ingest, query, lint and remediation endpoint families and the
Hub itself — and is registered as such. No new assembly, project, or port is introduced; namespace-level
containment enforced by an architecture test is sufficient, exactly as Principle I permits until an
ADR establishes a stronger boundary.

## Structural Rules Introduced

Classified per Constitution v1.11.0 (ADR-024's worked example is the model).

| Rule | Classification | Statement | Enforcement |
|---|---|---|---|
| **BR1** | **Boundary Rule** | Error-producing result factories — `Results`/`TypedResults` `.BadRequest`, `.Conflict`, `.NotFound`, `.UnprocessableEntity`, `.Problem`, `.ValidationProblem`, `.Json`, `.StatusCode` — MUST NOT be called from any `Grimoire.Hub` namespace other than `Grimoire.Hub.ApiErrors`. | Phase 0 `Grimoire.ArchTests` Mono.Cecil IL scan with a Red/Green probe, before feature code. Survey confirms all five current `Results.Json` call sites are error paths and `TypedResults` is unused, so the rule needs no allow-list. |
| **FSI1** | Feature-Scoped Invariant | Every catalogue entry has a non-empty title and detail; no detail contains its own code; every code an endpoint emits exists in the catalogue. | Classicist integration test over the real catalogue and real HTTP responses. No reflection over type shape. |
| **FSI2** | Feature-Scoped Invariant | Every UI surface that can display a request failure renders through the shared presentation. | Classicist Vitest behavioural test per surface, asserting the shared presentation's observable structure — never a component count or literal enumeration. Backed structurally by deleting the helpers there is nothing to fall back to. |

BR1 is a durable dependency-direction rule: it holds regardless of how any endpoint family's surface
grows, and every future endpoint inherits it. FSI1 and FSI2 protect this feature's current surface
shape and would be expected to change when that shape changes, which is exactly why neither gets a
reflection/IL-based test.

## Complexity Tracking

No Constitution Check violations. One note for the complexity gate rather than the constitution:
`IngestSubmissionEndpoints.cs` currently hosts the largest cluster of error call sites, and migrating
them removes branching rather than adding it — the `ToErrorResult` switch collapses into catalogue
lookups. The gate's delta rule should therefore see an improvement, not a regression, in the files
this feature touches.
