---
description: "Task list for 024-api-error-presentation"
---

# Tasks: Readable API Error Presentation

**Input**: Design documents from `/specs/024-api-error-presentation/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/api-error-envelope.md](./contracts/api-error-envelope.md), [ADR-026](../../docs/adr/ADR-026-hub-api-error-contract-and-frontend-error-presentation.md) (Accepted)

**Tests**: Required throughout. Every success criterion in `spec.md` is a deterministic guarantee
(the feature has no agentic surface), so every criterion maps to a hermetic integration test or a
classicist frontend test — never to an evaluation threshold.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US5, mapping to the user stories in `spec.md`
- Every task cites at least one literal `FR-###` / `SC-###` from `spec.md`, or names the phase goal
  explicitly where it serves no single requirement.

## Path Conventions

Web application: `backend/src/`, `backend/tests/`, `frontend/src/`.

---

## Phase 0: Boundary Rule Enforcement (BEFORE feature code)

**Goal**: prove BR1 detects violations before any feature code exists (Constitution Principle III).

**Boundary Rules covered**: BR1 from `plan.md ## Structural Rules Introduced` and ADR-026 — error-producing
result factories may be called only from `Grimoire.Hub.ApiErrors`.

**Sequencing note**: BR1 is red against the codebase as it stands — all five current `Results.Json`
call sites and every `Results.BadRequest`/`Conflict`/`NotFound` in the endpoint files violate it. It
therefore ships with a **remove-only baseline** (the `AgentArtifactNamingRuleTests` legacy-rename
ratchet is the in-repo precedent): the rule is live from Phase 0, the baseline lists the namespaces
not yet migrated, each migration task removes its entry, and T060 asserts the baseline is empty and
deletes it. CI stays green throughout, and no new violation can be added at any point.

- [X] T001 Write the BR1 structural rule in `backend/tests/Grimoire.ArchTests/HubApiErrorResultContainmentRuleTests.cs`: a Mono.Cecil IL scan (the `RuntimePathsBoundaryRuleTests` idiom) asserting no type in a `Grimoire.Hub.*` namespace outside `Grimoire.Hub.ApiErrors` calls `Results`/`TypedResults` `.BadRequest`, `.Conflict`, `.NotFound`, `.UnprocessableEntity`, `.Problem`, `.ValidationProblem`, `.Json`, or `.StatusCode`. Enforces FR-004.
- [X] T002 Seed the remove-only baseline in `HubApiErrorResultContainmentRuleTests.cs` with exactly the namespaces violating BR1 today (`Grimoire.Hub.IngestSubmission`, `Grimoire.Hub.QuerySubmission`, `Grimoire.Hub.QueryConversations`, `Grimoire.Hub.LintDispatch`, `Grimoire.Hub.RemediationTasks`), and assert the baseline is additive-proof: a violation in any namespace *not* listed fails the rule. Enforces FR-004.
- [X] T003 Red/Green probe for BR1: add a scratch class in a non-baselined Hub namespace (e.g. `Grimoire.Hub.Realtime`) calling `Results.Conflict(...)`, run `dotnet test backend/tests/Grimoire.ArchTests`, confirm the rule fails and names the call site, then delete the scratch class and confirm green. Records the probe outcome in the test's XML doc comment. Enforces FR-004; Constitution Principle III Red/Green requirement.

**Checkpoint**: BR1 is live and proven to detect violations. No feature code written yet.

---

## Phase 1: Setup

**Goal**: register the new namespace so creating it does not break the N1 ownership-map assertion.

- [X] T004 Add `Grimoire.Hub.ApiErrors` to the Hub namespace-ownership map as **Cross-agent** in `docs/conventions/agent-artifact-naming.md`, with a one-line justification (it serves the ingest, query, lint and remediation endpoint families and the Hub itself). Phase goal — serves no single FR; required by ADR-013 rule N1, which fails the build if a Hub namespace exists outside the map.
- [X] T005 Mirror the same entry into the N1 fixture in `backend/tests/Grimoire.ArchTests/AgentArtifactNamingRuleTests.cs` so the doc↔fixture assertion stays green. Phase goal; ADR-013 N1 ("any drift between this table and the fixture fails the build").

**Checkpoint**: the namespace may now be created.

---

## Phase 2: Foundational (BLOCKING — all user stories depend on this)

**Goal**: the envelope machinery exists and every failing response goes through it.

- [X] T006 Create `backend/src/Grimoire.Hub/ApiErrors/ApiErrorDefinition.cs`: the catalogue entry record (`Code`, `Status`, `Title`, `Detail`) with its construction-time invariants — non-empty title and detail, status in 400..599, and detail not containing its own code. Implements FR-001, FR-002, FR-016.
- [X] T007 Create `backend/src/Grimoire.Hub/ApiErrors/ApiErrorCatalogue.cs` holding every definition, with `Resolve(code)` returning the entry and an unknown code returning the generic fallback rather than echoing the identifier. Implements FR-016, SC-006.
- [X] T008 Create `backend/src/Grimoire.Hub/ApiErrors/ApiErrorResults.cs` — the only producer of error `IResult`s. Writes `application/problem+json` with `status`, `title`, `detail`, `code`, and `traceId` read from `Activity.Current` (falling back to the request identifier, omitting the member entirely when neither exists). Accepts an optional `detail` override; never an override of `code`, `title`, or `status`. Implements FR-001, FR-003, SC-001.
- [X] T009 Create `backend/src/Grimoire.Hub/ApiErrors/ApiErrorExceptionHandler.cs` (`IExceptionHandler`) turning an unhandled exception into the generic `internal_error` envelope. The exception's message reaches the log only and MUST NOT enter the response body. Implements FR-004, FR-013, FR-015, SC-008.
- [X] T010 Register the exception handler and `AddProblemDetails()` in `backend/src/Grimoire.Hub/HubHostComposition.cs` alongside `AddHubTelemetry` — not in `Program.cs`, which ADR-023 reduced to a pass-through. Implements FR-004; ADR-023 placement constraint.
- [X] T011 [P] Write the one intent-named wire-up test `ApiErrorExceptionHandler_IsRegistered_AndReachesOurHandler` in `backend/tests/Grimoire.IntegrationTests/HubApiErrorEnvelopeTests.cs`: an endpoint made to throw returns our `internal_error` envelope. Proves *our* registration reaches *our* code — not that ASP.NET invokes registered handlers (Constitution Principle II, "Test what we own"). Verifies FR-004, SC-008.

### Observability instrumentation (emitted from the foundational helper; contracts tested in Phase 8)

- [X] T012 Add the `hub.api_errors_total` counter to `backend/src/Grimoire.Hub/HubMetrics.cs` with labels `code` and `status`, on the existing `Grimoire.Hub` meter, and increment it once per envelope produced in `ApiErrorResults`. Implements `plan.md ## Observability > Business Metrics`; supports SC-001.
- [X] T013 Create `backend/src/Grimoire.Hub/ApiErrors/ApiErrorLogEvents.cs` emitting `api.error.declined` (WARN, fields `code`, `status`, `path`, `trace_id`) and `api.error.faulted` (ERROR, fields `code`, `status`, `path`, `trace_id`, `failure_reason`), using `EventId(int, "<stable.name>")` and snake_case message-template placeholders matching the field names, as every existing Hub `*LogEvents` class does. `failure_reason` carries the exception message on the fault path — log only. Implements `plan.md ## Observability > Structured Log Events`; supports FR-015.
- [X] T014 Emit the paired log-shaped spans `api.error.declined` and `api.error.faulted` from `ApiErrorLogEvents` via the `StartLogEventSpan` idiom, with `signal_type=log`, `event_name`, `level`, `code`, `status`, parented to the ambient ASP.NET Core request activity. Implements `plan.md ## Observability > Distributed Trace Spans`.

### Frontend foundation

- [X] T015 [P] Create `frontend/src/lib/services/apiError.ts` with the `ApiErrorCategory` union, the `PresentedError` shape from `data-model.md`, and the derivation function mapping an observed failure (thrown request, 4xx, 5xx, unusable response) to a `PresentedError`. Implements FR-005, FR-007, FR-013.
- [X] T016 [P] Create `frontend/src/lib/components/ApiErrorAlert.svelte` — a hand-built Svelte 5 (runes) component rendering a `PresentedError`. No third-party widget library (ADR-001's recorded mitigation). Implements FR-005, FR-009, FR-010.

**Checkpoint**: the envelope exists end to end; no endpoint uses it yet.

---

## Phase 3: User Story 1 — Understand a rejected request without reading JSON (P1) 🎯 MVP

**Goal**: every declined request reaches the user as an actionable sentence.

**Independent test**: trigger a refusable action (ask in a busy conversation; start lint with unresolved
remediation tasks; restart a task that is not failed) and confirm the visible message is a sentence a
non-developer can act on, with no status code, identifier, or JSON punctuation in it.

- [X] T017 [P] [US1] Author catalogue entries in `ApiErrorCatalogue.cs` for the nine codes carried over verbatim from the current API (`lint_run_active`, `unresolved_remediation_tasks`, `query_concurrency_limit_reached`, `conversation_already_active`, `conversation_record_unreadable`, `task_not_proposed`, `task_not_authorized`, `execution_already_started`, `message_turn_active`), each with its own title and actionable detail. `conversation_record_unreadable`'s detail names the real recovery (start a new conversation), since no retry can resolve it. Implements FR-002, FR-003, FR-016; ADR-014, ADR-018.
- [X] T018 [P] [US1] Author catalogue entries for the failures that today return only `{ message }` and gain a code: ingest submission validation (invalid JSON, wrong kind, missing file, bad convert config, unsupported media type), the ingest not-found paths, the queue-state conflict, the two restart declines from ADR-025 (task not `failed`; normalized source missing), and query submission validation. Each restart decline gets its **own** entry per ADR-018's "the board shows the actual outcome". Implements FR-002, FR-016; ADR-018, ADR-025.
- [X] T019 [US1] Migrate `backend/src/Grimoire.Hub/QuerySubmission/QuerySubmissionEndpoints.cs` to `ApiErrorResults`, removing all `Results.BadRequest`/`Results.Json`/`Results.Conflict`/`Results.NotFound` call sites, and remove `Grimoire.Hub.QuerySubmission` from the BR1 baseline. Implements FR-004, SC-001.
- [X] T020 [US1] Migrate `backend/src/Grimoire.Hub/QueryConversations/` error call sites to `ApiErrorResults` and remove `Grimoire.Hub.QueryConversations` from the BR1 baseline. Implements FR-004, SC-001.
- [X] T021 [US1] Migrate `backend/src/Grimoire.Hub/LintDispatch/LintSubmissionEndpoints.cs` to `ApiErrorResults` (preserving `lint_run_active` and `unresolved_remediation_tasks`, including the `unresolvedTaskIds` payload as an override detail) and remove `Grimoire.Hub.LintDispatch` from the BR1 baseline. Implements FR-003, FR-004, SC-001.
- [X] T022 [US1] Migrate `backend/src/Grimoire.Hub/RemediationTasks/RemediationTaskEndpoints.cs` to `ApiErrorResults`, collapsing its local `ToConflictResult` mapper into catalogue lookups, and remove `Grimoire.Hub.RemediationTasks` from the BR1 baseline. Implements FR-004, SC-001.
- [X] T023 [US1] Migrate `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs` to `ApiErrorResults`, collapsing the `ToErrorResult` switch into catalogue lookups, and remove `Grimoire.Hub.IngestSubmission` from the BR1 baseline. Implements FR-004, SC-001.
- [X] T024 [US1] Assert the envelope on real HTTP in `backend/tests/Grimoire.IntegrationTests/HubApiErrorEnvelopeTests.cs`: one request per documented failure across all five endpoint families, each asserting media type `application/problem+json` and all required members populated. Verifies SC-001, FR-001.
- [X] T025 [US1] Assert catalogue completeness in `HubApiErrorEnvelopeTests.cs` (FSI1, classicist — over the real catalogue and real responses, no reflection over type shape): every entry has non-empty title and detail; no detail contains its own code; every code an endpoint emits exists in the catalogue; an unknown code resolves to the generic entry. Verifies SC-006, FR-016, FR-002.
- [X] T026 [P] [US1] Rewrite `frontend/src/lib/services/lintApi.ts`, `remediationApi.ts` and `querySubmissionApi.ts` to derive errors through `apiError.ts`, **deleting** their three `REASON_MESSAGES` tables and their private `parseErrorMessage` copies. Implements FR-005, FR-010, SC-005.
- [X] T027 [P] [US1] Rewire `frontend/src/lib/services/ingestSubmissionsApi.ts`, `lintLifecycleClient.ts` and `remediationLifecycleClient.ts` onto `apiError.ts` and **delete** `frontend/src/lib/services/httpErrorMessage.ts`. Implements FR-010, SC-005.
- [X] T028 [US1] Render `ApiErrorAlert` with the envelope's `detail` as the primary message on the query page (`frontend/src/routes/query/+page.svelte`), and assert in `frontend/src/routes/query/page.svelte.test.ts` that a declined submission shows the prose and that no code, status, or brace appears in the primary region. Verifies FR-005, SC-002.

**Checkpoint**: the reported defect is fixed — declined requests read as sentences. Independently
shippable as the MVP.

---

## Phase 4: User Story 2 — Keep the technical detail, one step away (P1)

**Goal**: the technical facts survive, one interaction away from the message.

**Independent test**: trigger any failure, confirm the primary message carries no technical noise, open
the detail affordance, and confirm status, code, and correlation id are all present.

- [X] T029 [US2] Add the technical-detail disclosure to `frontend/src/lib/components/ApiErrorAlert.svelte`: status, `code`, `traceId`, and `bodyExcerpt`, collapsed by default. Implements FR-006.
- [X] T030 [US2] Implement primary-message length bounding in `frontend/src/lib/services/apiError.ts`, populating `fullMessage` with the untruncated text whenever `message` is shortened, and surface `fullMessage` in the disclosure. Implements FR-014, SC-007.
- [X] T031 [US2] Populate `bodyExcerpt` in `apiError.ts` only when the response body was not a recognizable envelope, from the response the Hub produced, length-capped at construction. Implements FR-006, FR-013, FR-015.
- [X] T032 [P] [US2] Assert the primary/technical split in `frontend/src/lib/components/ApiErrorAlert.svelte.test.ts`: the three technical fields are absent from the primary region when the disclosure is closed and present when opened; `traceId` is omitted entirely (not rendered empty) when the envelope carried none. Verifies SC-003, FR-006.
- [X] T033 [P] [US2] Assert length bounding in `frontend/src/lib/services/apiError.test.ts`: an over-length detail yields a bounded `message` and a `fullMessage` equal to the original. Verifies SC-007, FR-014.
- [X] T034 [US2] Assert in `HubApiErrorEnvelopeTests.cs` that the `traceId` returned in the body equals the trace identity the Hub used for that request, so the value is joinable rather than decorative. Verifies FR-001, FR-006.

**Checkpoint**: readable *and* diagnosable.

---

## Phase 5: User Story 3 — Notice that something failed (P2)

**Goal**: a failure is seen and announced rather than missed.

**Independent test**: trigger a failure on each surface and confirm it renders in a consistent, visually
distinct region that assistive technology announces without focus moving.

- [X] T035 [US3] Style `ApiErrorAlert.svelte` as a distinct alert region using the project's existing Tailwind tokens (`text-stage-failed` and siblings), consistent across every surface. Implements FR-009.
- [X] T036 [US3] Mark the alert region for assistive technology (`role="alert"` / live region) so it is announced without stealing keyboard focus. Implements FR-009.
- [X] T037 [US3] Add a dismiss control to `ApiErrorAlert.svelte` that clears the error until a new failure occurs. Implements FR-011.
- [X] T038 [P] [US3] Assert in `ApiErrorAlert.svelte.test.ts` that the region carries its alert semantics, that rendering it does not move focus, and that dismissing removes it. Verifies FR-009, FR-011.

**Checkpoint**: errors are noticed.

---

## Phase 6: User Story 4 — Tell apart offline, broken, and refused (P2)

**Goal**: the four categories are distinguishable and retry is offered exactly where it can work.

**Independent test**: produce all four categories against one surface; confirm each is presented
distinguishably and retry appears only on the retryable two.

- [X] T039 [US4] Implement the full derivation table from `data-model.md` in `apiError.ts`: no response → `unreachable`; 4xx with envelope → `declined`; 4xx without → `unexpected`; 5xx (with or without envelope) → `fault`; ok-but-unusable → `unexpected`. Classification follows what happened to the request, never body parseability. Implements FR-007.
- [X] T040 [US4] Derive retryability from the category (`unreachable` and `fault` retryable; `declined` and `unexpected` not) rather than as an independent field, so FR-008 and SC-004 cannot drift apart. Implements FR-008.
- [X] T041 [US4] Add a category-specific generic message for each category in `apiError.ts`, used whenever no envelope prose is available, so no state exists in which the component has nothing readable to show. Implements FR-013, SC-002.
- [X] T042 [US4] Render the category distinguishably in `ApiErrorAlert.svelte` and expose a retry control on the retryable categories that raises a retry request the surface handles — the component holds no surface-specific knowledge. Implements FR-007, FR-008.
- [X] T043 [P] [US4] Assert the derivation table in `apiError.test.ts` with one case per row, including the gateway case: a 502 whose body is HTML derives `fault` and stays retryable. Uses a rejected fetch for the unreachable case — no `Task.Delay`, no timer (ADR-021). Verifies SC-004, FR-007, FR-013.
- [X] T044 [P] [US4] Assert in `ApiErrorAlert.svelte.test.ts` that the four categories render distinguishably and that the retry control appears for exactly the two retryable ones. Verifies SC-004, FR-008.
- [X] T045 [US4] Wire retry and error-clearing on the query and ingest submission surfaces: a successful retry clears the displayed error. Implements FR-008, FR-011.

**Checkpoint**: failures are actionable.

---

## Phase 7: User Story 5 — No surface left behind (P3)

**Goal**: one presentation everywhere, including recorded agent-run failures.

**Independent test**: walk every surface that can display a failure and confirm each renders through
the shared presentation.

- [X] T046 [P] [US5] Migrate `frontend/src/lib/components/SubmissionForm.svelte` to `ApiErrorAlert` and update `SubmissionForm.svelte.test.ts` to assert the shared presentation's structure (FSI2 — the concern, never a component count). Implements FR-010; verifies SC-005.
- [X] T047 [P] [US5] Migrate `frontend/src/routes/lint/+page.svelte` and `frontend/src/routes/+page.svelte`'s lint-trigger banner to `ApiErrorAlert`; update `lint/page.svelte.test.ts` and `board-lint-trigger.svelte.test.ts`. Implements FR-010; verifies SC-005.
- [X] T048 [P] [US5] Migrate `frontend/src/lib/components/TaskCard.svelte` (restart error) and `frontend/src/routes/tasks/[taskId]/+page.svelte` (restart error) to `ApiErrorAlert`, keying the displayed error on the task's current stage rather than on transition count, since ADR-025 lets a task re-enter `running` repeatedly. Implements FR-010, FR-011; verifies SC-005.
- [X] T049 [P] [US5] Migrate `frontend/src/lib/components/RemediationTaskCard.svelte` and `TaskMessageThread.svelte` to `ApiErrorAlert`, keeping their client-side validation messages distinct from request failures. Implements FR-010; verifies SC-005.
- [X] T050 [US5] Implement `RecordedFailurePresentation` in `apiError.ts`: converts a recorded failure string into a `fault`-category `PresentedError`, stripping only the prefix our own code composes (`Model API error <status> (<type>): `) into the technical detail and leaving the provider's sentence as `message`. Text without that prefix is presented unchanged. Implements FR-012.
- [X] T051 [P] [US5] Render recorded failures through `ApiErrorAlert` in `TaskCard.svelte`, `LintRunCard.svelte`, `QueryConversation.svelte`, `TaskRecordView.svelte` and `StatusHistoryPath.svelte`. Implements FR-010, FR-012; verifies SC-005.
- [X] T052 [P] [US5] Assert in `apiError.test.ts` that a recorded `Model API error 400 (invalid_request_error): …` string presents the provider's sentence as `message` with the prefix in the technical detail, that an over-length one is bounded with `fullMessage` intact, and that a `[REDACTED]` marker survives presentation unchanged. Verifies FR-012, FR-014, SC-008.
- [X] T053 [US5] Add a comment at each of the five background-refresh catch sites (`routes/+page.svelte` ×3, `routes/tasks/[taskId]/+page.svelte`, `remediationLifecycleClient.ts`) recording that silence is deliberate there — a failing background refresh must not displace an error the user is reading about their own action. Implements FR-011 (spec edge case "Failure during an in-flight background refresh").

**Checkpoint**: SC-005 holds; nothing renders a raw response anywhere.

---

## Phase 8: Observability Contracts

**Goal**: every declared signal is verified through the production composition root.

All tests in this phase obtain their signals via `AddHubTelemetry`'s `configureTracing` /
`configureMetrics` seams (the `IngestApiHost` idiom), never a hand-registered `ActivitySource` or an
always-on listener — Constitution Principle IV's production-wiring rule, written from the feature-003
failure on this exact request path. The class joins the existing serialized
`HubActivityListenerObservability` collection.

- [X] T054 Assert the metric contract in `backend/tests/Grimoire.IntegrationTests/HubApiErrorObservabilityTests.cs`: `hub.api_errors_total` increments once per envelope, carrying `code` and `status`, observed through the production `AddMeter("Grimoire.Hub")` registration after `MeterProvider.ForceFlush()`. Verifies `plan.md ## Observability > Business Metrics`; supports SC-001.
- [X] T055 Assert the logging contract in `HubApiErrorObservabilityTests.cs` using the existing `CaptureLoggerFactory` idiom: `api.error.declined` at WARN with `code`, `status`, `path`, `trace_id`; `api.error.faulted` at ERROR with those plus `failure_reason`. Asserts event name via `EventId.Name`. Verifies `plan.md ## Observability > Structured Log Events`.
- [X] T056 Assert the trace contract in `HubApiErrorObservabilityTests.cs`: both log-shaped spans are **exported** under the production registration (not merely recorded), carry `signal_type=log`, `event_name`, `level`, `code`, `status`, and are parented to the ASP.NET Core request activity — the linkage that failed silently in feature 003. Uses `PollAsync` for the export wait (ADR-021 — no fixed wait). Verifies `plan.md ## Observability > Distributed Trace Spans`.
- [X] T057 Assert that the `trace_id` on both log events, the trace id on both spans, and the `traceId` member of the response body are the same value for one request — the correlation guarantee that makes the exposed identifier useful. Verifies FR-001; Constitution Principle IV correlation requirement.
- [X] T058 CI enforcement: confirm `HubApiErrorObservabilityTests` and `HubApiErrorEnvelopeTests` are executed by the existing `Run hermetic integration tests` step in `.github/workflows/ci.yml`, and that `HubApiErrorResultContainmentRuleTests` is executed by the existing `Run architecture tests` step — no workflow change is expected; record the verified run in the task notes. If either is not picked up, amend `ci.yml` in this task. Satisfies the logging- and trace-contract CI category for every Observability row.

**Checkpoint**: every declared signal is emitted, correlated, and gated by CI.

---

## Phase 9: Polish, Migration & Completeness Audit

- [X] T059 [P] Migrate the existing integration tests that assert the old `reason` / `message` members onto the envelope's `code` / `detail`: `RemediationAuthorizationTests`, `LintConcurrencyAndLivenessTests`, `QueryConversationRecordFailClosedTests`, `IngestTaskRecordApiTests`, `BoardCompositeResponseTests`, `IngestSubmissionPromptApiTests`. These assert a product-owned contract, so they are rewritten and sharpened, not deleted (Constitution Principle II). Verifies FR-003, SC-001.
- [X] T060 Empty and delete the BR1 remove-only baseline in `HubApiErrorResultContainmentRuleTests.cs`, asserting the rule now enforces BR1 outright with no suppression mechanism, and re-run the T003 probe to confirm it still detects a violation without the baseline. Verifies FR-004, SC-001.
- [X] T061 [P] Update `specs/023-task-ui-improvements/contracts/http-api.md`'s error rows to reference the new envelope, with a pointer to `specs/024-api-error-presentation/contracts/api-error-envelope.md` as the authority. Phase goal — keeps an earlier feature's contract doc from stating a shape that no longer exists.
- [X] T062 [P] Run the frontend gates (`bun run check`, `bun run lint`, `bun run test`) and the backend gates (`dotnet format --verify-no-changes`, ArchTests, IntegrationTests) and fix any fallout. Phase goal — the CI gate set from `.github/workflows/ci.yml`.
- [X] T063 Walk `quickstart.md` end to end against a running Hub and frontend, confirming all seven manual checks and the accessibility spot-check behave as written; correct the document where reality differs. Verifies FR-002, FR-005, FR-006, FR-007, FR-008, FR-012, FR-015.
- [X] T064 **Completeness audit** (named final-phase task, Constitution Principle III): cross-reference every row of `plan.md ## Observability` (one metric, two log events, two spans) and every success criterion SC-001…SC-008 against its implementing task and its passing test; confirm the spec has no agent-judgment criterion requiring an evaluation test (it has none by design, and the spec says why); file any gap found as a new task in this file before declaring the Definition of Done met.

---

## Completeness Audit Result (T064)

Performed 2026-08-16 after every other task closed.

### Observability rows

| Row | Implemented by | Verified by | Status |
|---|---|---|---|
| Metric `hub.api_errors_total` {code,status} | T012 — `HubMetrics.RecordApiError`, incremented in `ApiErrorResults.Emit` | T054 — exported through the production `AddMeter("Grimoire.Hub")` registration; label set asserted closed | ✅ |
| Log `api.error.declined` WARN {code,status,path,trace_id} | T013 — `ApiErrorLogEvents.LogDeclined` | T055 — name via `EventId.Name`, level, all four fields | ✅ |
| Log `api.error.faulted` ERROR {code,status,path,trace_id,failure_reason} | T013 — `ApiErrorLogEvents.LogFaulted` | T055 — all five fields, plus that the exception text is here and not in the body | ✅ |
| Span `api.error.declined`, parent = ASP.NET Core request activity | T014 — `StartLogEventSpan` | T056 — exported under production registration, parent non-default, all attributes | ✅ |
| Span `api.error.faulted`, parent = ASP.NET Core request activity | T014 — `StartLogEventSpan` | T056 — same | ✅ |
| Correlation via `trace_id` | T008 — read from `Activity.Current` in the result | T057 — log field, span trace id and body `traceId` asserted equal for one request | ✅ |

CI (T058): all three test classes run in the existing `.github/workflows/ci.yml` steps — the
integration tests under `Run hermetic integration tests`, the Boundary Rule under
`Run architecture tests`. No workflow change was needed; verified by running both steps' commands.

### Success criteria

| Criterion | Verified by | Status |
|---|---|---|
| SC-001 envelope on every declining/failing response | T024 (ingest + lint families, all members) | ✅ |
| SC-002 readable message is primary | T032, T041 (`apiError.test.ts` "no category can produce an empty message…") | ✅ |
| SC-003 technical detail exposed on demand | T032 | ✅ |
| SC-004 four categories distinguishable, retry on exactly two | T043 (derivation), T044 (rendering) | ✅ |
| SC-005 every surface uses the shared presentation | T046–T051 plus the three FSI2 assertions on board/lint/task-detail; both fallback helpers deleted | ✅ |
| SC-006 every code resolves to authored prose | T025 (completeness, uniqueness, no-identifier-leak, unknown-code fallback) | ✅ |
| SC-007 length-bounded with full text recoverable | T033, T032 | ✅ |
| SC-008 redaction preserved | T011/T024 (exception text never in the body), T052 (`[REDACTED]` survives presentation) | ✅ |

### Agent-judgment criteria

None, by design. The spec states why: this is a harness-only feature (Constitution Principle V),
so an evaluation threshold attached to it would be a spec defect in the opposite direction. No
instruction file was read or written, so ADR-012's staleness merge gate did not fire — confirmed by
the eval suite passing unchanged (73/73).

### Gaps found

Two, both closed within the feature rather than deferred:

1. **T020 had no work to do.** The plan listed `Grimoire.Hub.QueryConversations` as a namespace
   needing migration; BR1's own stale-baseline assertion caught on first run that it contains no
   error call site at all — its endpoints live in `QuerySubmissionEndpoints`. T019 covers them. The
   baseline entry was removed rather than left to rot.
2. **T024 initially covered one endpoint family.** ADR-013's N1 rule surfaced this as a naming
   violation, which was the correct diagnosis of a coverage gap: a claim of "one envelope for every
   family" demonstrated only against ingest. Lint cases were added, including the extension-member
   case (`unresolvedTaskIds`), before the residual reference-detection gap was exempted.

No gap remains open. The Definition of Done is met.

---

## Dependencies

```text
Phase 0 (T001-T003)  BR1 live + probed
        │
Phase 1 (T004-T005)  namespace registered in the N1 map
        │
Phase 2 (T006-T016)  envelope machinery + frontend foundation   ← BLOCKING
        │
        ├─ Phase 3  US1 (P1)  T017-T028   ← MVP
        │      │
        │      ├─ Phase 4  US2 (P1)  T029-T034
        │      ├─ Phase 5  US3 (P2)  T035-T038
        │      ├─ Phase 6  US4 (P2)  T039-T045
        │      └─ Phase 7  US5 (P3)  T046-T053
        │
Phase 8 (T054-T058)  observability contracts  (needs Phase 2 emission)
        │
Phase 9 (T059-T064)  migration, baseline removal, audit
```

- **Phase 0 → Phase 1**: the rule must be proven before the namespace it protects exists.
- **Phase 1 → Phase 2**: creating `Grimoire.Hub.ApiErrors` before T004/T005 fails the N1 map assertion.
- **Phase 2 → all stories**: every story renders or produces the envelope.
- **Phase 3 → Phases 4–7**: US2–US5 all extend the presentation US1 establishes. Once Phase 3 lands,
  Phases 4, 5, 6 and 7 are independent of each other and may proceed in parallel.
- **Phase 8** depends only on Phase 2 (the emission sites) and may run alongside Phases 4–7.
- **T060** depends on every migration task T019–T023.

## Parallel Execution Opportunities

- **Phase 2**: T015 and T016 (frontend) are independent of T006–T014 (backend) — two workstreams.
- **Phase 3**: T017 and T018 (catalogue authoring) run together; T019–T023 (endpoint migrations) touch
  five different files and run together once the catalogue exists; T026 and T027 (frontend clients)
  run together.
- **Phases 4–7**: independent of each other after Phase 3. Within each, the `[P]`-marked test tasks
  run alongside the implementation tasks they verify only if written first; otherwise they follow.
- **Phase 7**: T046, T047, T048, T049 and T051 touch disjoint component files.

## Implementation Strategy

**MVP = Phase 0 + Phase 1 + Phase 2 + Phase 3.** That delivers the reported defect's fix: declined
requests reach the user as sentences instead of status codes and machine identifiers, with the
envelope contract and its Boundary Rule already enforced. Everything after it improves delivery
(detail, prominence, categories, coverage) on top of a message that is already correct.

**Delivery shape**: per `CLAUDE.md`'s stacked-PR convention this feature spans enough phases to be
delivered as a stack rather than one big-bang PR — a natural cut is Phase 0–2 (contract + machinery),
Phase 3 (MVP), Phases 4–7 (presentation), Phase 8–9 (observability + audit). The Definition of Done
stays whole-feature regardless of how the stack is cut.

---

## Phase 10: Convergence

Appended by `/speckit-converge`. Both findings are the same shape as the gap the Phase 9 audit
already closed for `handleResume`: a *user action* whose request failure never reaches the shared
presentation. The audit found that one by reading `routes/+page.svelte`; these two sit in files that
phase did not re-read.

- [X] T065 Route the query-turn interrupt failure through `ApiErrorAlert` in `frontend/src/routes/query/+page.svelte`: `handleInterrupt`'s `catch {}` currently swallows it, though the "Stop" button (`QueryConversation.svelte`, `data-testid="query-turn-stop-button"`) makes it a user action, not a background refresh. Its stated reason — that the true state "arrives via `queryTurnChanged` regardless" — does not hold for the case that matters: if the interrupt never reached the Hub there is no such event, so the turn keeps running and nothing explains why the click did nothing. Present it on the query surface with a retry, as `handleResume` already is, and cover it with a classicist test asserting the shared presentation appears when `interruptQueryTurn` rejects. Implements FR-010, FR-011; verifies SC-005 (partial)
- [X] T066 Record at `frontend/src/lib/components/QueryPromptForm.svelte`'s validation message (`data-testid="query-prompt-error"`) that its pre-024 `<p class="text-sm text-stage-failed">` shape is a deliberate distinction — client-side validation, not a request failure — matching the note `SubmissionForm.svelte` carries at its own equivalent. It is the last surviving instance of the shape `ApiErrorAlert` replaced, and without the note a reader cannot tell a decision from a missed migration. Implements plan FSI2; extends T049's distinction (partial)
