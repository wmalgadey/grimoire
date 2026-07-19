# Tasks: Hexagonal Architecture Alignment & Task Detail Markdown View

**Input**: Design documents from `/specs/006-hexagonal-arch-tasks-ui/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md — all present. ADR-010 is **Accepted**.

**Tests**: Included. Every success criterion is a deterministic harness guarantee (no agentic surface), so all verification is deterministic: NetArchTest structural rules, hermetic integration tests (in-memory OTel exporter, temp dirs, fakes), and Vitest browser component tests. No agent-behavior evaluation tests are required for this feature.

**Organization**: Tasks are grouped by user story (US1 = P1 hexagonal conformance, US2 = P2 rendered detail view, US3 = P3 live updates).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 — user story phases only
- Every task names exact file paths

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III, ADR-010)

**Purpose**: Write the ADR-010 structural rules before any feature code. This codebase is a **remediation target**: rules C2–C5 and the new-port presence checks have *genuine active violations* today, so they start RED — that Red is real, not synthetic. They turn Green through the US1 restructuring, and each rule's synthetic Red/Green probe (temporary violating class) then proves the guard stays live. Rule C1 has no active violation and is probed immediately.

**⚠️ NON-NEGOTIABLE**: No feature implementation (Phase 3+) may begin before T001–T002 exist and T001's probe is documented.

- [X] T001 Create `backend/tests/Grimoire.ArchTests/HexagonalPortsAdapterRuleTests.cs` with rule C1 (`Microsoft.Data.Sqlite` types referenced only from `Grimoire.Hub.OperationalState`; per contracts/ports-and-adapters.md) and execute its Red/Green probe: add a temporary class in `backend/src/Grimoire.Hub/Conversion/` that references `SqliteConnection`, run `dotnet test backend/tests/Grimoire.ArchTests` (MUST fail), delete the probe class, re-run (MUST pass). Document the probe in the commit message.
- [X] T002 Extend `backend/tests/Grimoire.ArchTests/HexagonalPortsAdapterRuleTests.cs` with rules C2–C5 and port-presence checks from contracts/ports-and-adapters.md: C2 (`Anthropic` SDK only in `Grimoire.IngestAgent.AgentCore.Adapters.Anthropic`), C3 (Hub `System.Net.Http` client usage only in `Grimoire.Hub.IngestSubmission.Adapters.HttpFetch`; `Program.cs`/`TelemetryExtensions.cs` composition-root exempt), C4 (Hub `System.Diagnostics.Process` usage only in `Grimoire.Hub.AgentDispatch.Adapters.AgentProcess` and `Grimoire.Hub.IngestSubmission.Adapters.MarkItDown`), C5 (types outside an `.Adapters.` segment never reference `AgentProcessHost`, `MarkItDownConverter`, `UrlContentFetcher`, `AnthropicModelClient`; composition roots exempt), plus presence/ownership checks that `IMarkdownConverter` and `IUrlContentFetcher` exist in `Grimoire.Hub.IngestSubmission` and each P1–P4 production adapter implements its port. Orchestration-side rules MUST carve out `.Adapters.` sub-namespaces (ADR-010 rule-authoring note). Run the suite and record the expected failures (the pre-existing violations: `AnthropicModelClient` in `AgentCore`, `UrlContentFetcher`/`MarkItDownConverter` in `Conversion`, `AgentProcessHost` in `AgentDispatch` root, `SubmissionService` → `AgentProcessHost`, missing new ports) as the documented Red state.

**Checkpoint**: All ADR-010 rules exist and run in `Grimoire.ArchTests`. C1 is proven live. C2–C5/port-presence are RED against the documented pre-existing violations — US1 exists to turn them Green.

---

## Phase 1: Setup

**Purpose**: Record the pre-change baseline that SC-003 (zero regression) is measured against.

- [X] T003 Record the green baseline: run `dotnet test` in `backend/` and `npm run test` in `frontend/` on the unmodified codebase (excluding the expected-Red rules from T002) and note the passing test counts in the T003 commit message as the SC-003 reference point.

---

## Phase 2: Foundational (Blocking Prerequisites)

No foundational tasks. The Phase 0 rules are the only cross-story prerequisite; the three stories share no other blocking infrastructure (US2/US3 depend on US1 only via the namespaces it establishes).

---

## Phase 3: User Story 1 — Codebase Conforms to the Hexagonal Boundary Rules (Priority: P1) 🎯 MVP

**Goal**: Ports for all four replaceable external systems, production adapters moved into context-nested `<Consumer>.Adapters.<System>` namespaces, orchestration consuming ports only, everything enforced by the Phase 0 rules — with zero behavioral change (move-only + interface extraction).

**Independent Test**: `dotnet test backend/tests/Grimoire.ArchTests` passes with zero active violations; each rule demonstrably fails when a probe violation is introduced and passes after removal (quickstart §1); the full pre-existing suite still passes offline without API keys (quickstart §2).

### Ports (extract/define first — consumers and adapters compile against these)

- [X] T004 [US1] Extract the `IAgentProcessLauncher` interface out of `backend/src/Grimoire.Hub/AgentDispatch/AgentProcessHost.cs` into its own file `backend/src/Grimoire.Hub/AgentDispatch/IAgentProcessLauncher.cs` and extend it with the `RunToExitAsync` capability that `SubmissionService` currently uses on the concrete `AgentProcessHost` (research.md Decision 3; keep the contract hermetic-fake-friendly).
- [X] T005 [P] [US1] Define the `IMarkdownConverter` port in `backend/src/Grimoire.Hub/IngestSubmission/IMarkdownConverter.cs` (`ConvertAsync(inputPath) → MarkItDownConversionResult` per data-model.md; the existing `MarkItDownConversionResult` record becomes part of the port contract).
- [X] T006 [P] [US1] Define the `IUrlContentFetcher` port in `backend/src/Grimoire.Hub/IngestSubmission/IUrlContentFetcher.cs` (`FetchAsync(url) → UrlFetchResult` per data-model.md; the existing `UrlFetchResult` record becomes part of the port contract).

### Adapter moves (move-only commits — no logic changes)

- [X] T007 [US1] Move `backend/src/Grimoire.Hub/AgentDispatch/AgentProcessHost.cs` and `backend/src/Grimoire.Hub/AgentDispatch/LocalSecretsLoader.cs` to `backend/src/Grimoire.Hub/AgentDispatch/Adapters/AgentProcess/` with namespace `Grimoire.Hub.AgentDispatch.Adapters.AgentProcess`; `AgentProcessHost` implements the extended `IAgentProcessLauncher`. Per-agent credential env scoping must remain byte-identical (ADR-004).
- [X] T008 [P] [US1] Move `backend/src/Grimoire.Hub/Conversion/MarkItDownConverter.cs` and `backend/src/Grimoire.Hub/Conversion/MarkItDownOptions.cs` to `backend/src/Grimoire.Hub/IngestSubmission/Adapters/MarkItDown/` with namespace `Grimoire.Hub.IngestSubmission.Adapters.MarkItDown`; `MarkItDownConverter` implements `IMarkdownConverter`.
- [X] T009 [P] [US1] Move `backend/src/Grimoire.Hub/Conversion/UrlContentFetcher.cs` to `backend/src/Grimoire.Hub/IngestSubmission/Adapters/HttpFetch/` with namespace `Grimoire.Hub.IngestSubmission.Adapters.HttpFetch`; `UrlContentFetcher` implements `IUrlContentFetcher`.
- [X] T010 [P] [US1] Move `backend/src/Grimoire.IngestAgent/AgentCore/AnthropicModelClient.cs` to `backend/src/Grimoire.IngestAgent/AgentCore/Adapters/Anthropic/` with namespace `Grimoire.IngestAgent.AgentCore.Adapters.Anthropic` (`AgentLoop` already consumes `IModelClient` only — no consumer change needed).

### Consumer switches & composition-root wiring

- [X] T011 [US1] Switch `backend/src/Grimoire.Hub/Submission/SubmissionService.cs` from the concrete `AgentProcessHost` to the `IAgentProcessLauncher` port (constructor injection; no behavioral change).
- [X] T012 [US1] Switch `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionPipeline.cs` (and `ConvertStepRegistry.cs` if it constructs the concrete types) from `MarkItDownConverter`/`UrlContentFetcher` to the `IMarkdownConverter`/`IUrlContentFetcher` ports.
- [X] T013 [US1] Update composition roots: `backend/src/Grimoire.Hub/Program.cs` binds `IAgentProcessLauncher → AgentProcessHost`, `IMarkdownConverter → MarkItDownConverter`, `IUrlContentFetcher → UrlContentFetcher` (only place referencing concrete adapter types); `backend/src/Grimoire.IngestAgent/Program.cs` updates the `AnthropicModelClient` using/namespace reference.

### Test fakes & suite alignment

- [X] T014 [P] [US1] Create `backend/tests/Grimoire.IntegrationTests/Fakes/FakeMarkdownConverter.cs` implementing `IMarkdownConverter` (scriptable success/failure results, no subprocess).
- [X] T015 [P] [US1] Create `backend/tests/Grimoire.IntegrationTests/Fakes/FakeUrlContentFetcher.cs` implementing `IUrlContentFetcher` (scriptable fetch results, no network).
- [X] T016 [US1] Align the existing test suites with the restructuring: update namespace references across `backend/tests/` after the moves; switch orchestration-level tests that construct concrete adapters (`backend/tests/Grimoire.IntegrationTests/Fakes/IngestSubmissionPipelineFixture.cs`, `ConvertStepTests.cs`) to the new fakes via ports; adapter-focused tests (`UrlContentFetcherTests.cs`) keep exercising the real adapter through its port type. No assertion weakening — SC-003 forbids behavioral change.

### Conformance gate (turns Phase 0 Green, proves liveness)

- [X] T017 [US1] Run `dotnet test backend/tests/Grimoire.ArchTests`: all rules from T001–T002 plus all pre-existing rules (`DomainDependencyRuleTests`, `GuardedWriteBoundaryRuleTests`, `HubAgentDispatchBoundaryRuleTests`, `NonBlockingDispatchRuleTests`, `RuntimePathsBoundaryRuleTests`) pass with zero active violations; adjust pre-existing rules' namespace references if the moves shifted them, without weakening any rule.
- [X] T018 [US1] Execute the synthetic Red/Green probes for C2, C3, C4, C5 and port ownership (quickstart §1 protocol): for each rule add one temporary deliberately-violating class (e.g. a class in `backend/src/Grimoire.Hub/IngestSubmission/` that news up `MarkItDownConverter` for C5), verify `dotnet test backend/tests/Grimoire.ArchTests` fails naming that rule, delete the probe, verify it passes. Document each probe result in the commit message. No probe file may remain (spec Edge Cases).
- [X] T019 [US1] Regression & hermeticity gate: run the full `dotnet test` in `backend/` and `npm run test` in `frontend/` offline with no `ANTHROPIC_API_KEY` set — everything from the T003 baseline passes (SC-003) with zero live LLM/network access (SC-002).

**Checkpoint**: US1 complete — constitution Principle I holds and is machine-enforced. Independently shippable as the MVP.

---

## Phase 4: User Story 2 — Task Details Show the Rendered Task Record (Priority: P2)

**Goal**: "Details" on a task card opens `/tasks/[taskId]` showing the task record's parsed metadata header and its markdown body rendered via `marked`+`dompurify`; missing/unreadable record shows a placeholder; machine-readable endpoints unchanged.

**Independent Test**: quickstart §3 — open the board, click Details on a task with a record: metadata header + formatted markdown, no raw JSON/frontmatter; `curl` of `/api/ingest-submissions/{taskId}/task-record` matches the contract; deleting the record file yields the placeholder.

### Tests for User Story 2 (write first — they MUST fail before implementation)

- [X] T020 [P] [US2] Create `backend/tests/Grimoire.IntegrationTests/TaskRecordApiTests.cs` (hermetic, in-proc Hub host, temp `TasksDir` fixtures): valid v2 record → 200 with `metadata` (all data-model.md fields incl. `null` serialization) + `body` with frontmatter stripped; missing file → 404 problem payload `{"message": ...}`; malformed/torn frontmatter → 404 (never 5xx); unknown board taskId → 404; and byte-for-byte invariance of the existing `GET /api/ingest-submissions/{taskId}` and `/board` responses (contracts/task-record-api.md).
- [X] T021 [P] [US2] Create `frontend/src/lib/components/TaskRecordView.svelte.test.ts` (Vitest browser, fetch stub): rendered markdown output (headings, lists, emphasis, code blocks as HTML elements, no raw `---` block, no raw markdown source), metadata header rendered distinctly (status, timestamps, refs), placeholder state on 404/"unavailable", and a representative full-lifecycle record fixture readable end-to-end (SC-006).

### Backend implementation for User Story 2

- [X] T022 [US2] Create the TaskRecord read model in `backend/src/Grimoire.Hub/IngestSubmission/TaskRecordReadModel.cs`: resolve `<TasksDir>/{taskId}.md` exclusively via `ResolvedGrimoirePaths` (ADR-009), parse frontmatter with the existing `TaskArtifactFrontmatter.TryParse`, strip the frontmatter block, map to the data-model.md read-model shape; missing file or parse failure → "unavailable" result (no exception).
- [X] T023 [US2] Add `GET /api/ingest-submissions/{taskId}/task-record` to `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs` per contracts/task-record-api.md: 200 JSON `{taskId, metadata, body}`, 404 problem payload for unavailable records, existing endpoints untouched (FR-012).
- [X] T024 [US2] Instrument the serve path (implementation per plan.md ## Observability; verification tests are Phase 6): `task_record.served` INFO log event with `task_id`, `outcome`, `content_length` in `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionLogEvents.cs`; counter `hub.task_record_reads_total` with `outcome=ok|missing|unparseable` label in `backend/src/Grimoire.Hub/HubMetrics.cs`; span `hub.task_record.serve` (child of the ASP.NET Core request span, attributes `task_id`, `outcome`) in `backend/src/Grimoire.Hub/HubTracing.cs`; log and counter emitted within the span context.

### Frontend implementation for User Story 2

- [X] T025 [P] [US2] Add `marked` and `dompurify` as runtime dependencies in `frontend/package.json` (`npm install marked dompurify`; both ship their own types).
- [X] T026 [P] [US2] Add the `TaskRecord` type (mirror of the data-model.md read model) to `frontend/src/lib/types.ts`.
- [X] T027 [US2] Add `getTaskRecord(taskId)` to `frontend/src/lib/services/ingestSubmissionsApi.ts` (returns `TaskRecord` on 200, an "unavailable" discriminant on 404).
- [X] T028 [US2] Create `frontend/src/lib/components/TaskRecordView.svelte`: metadata header (status, timestamps, source refs, failure reason when present) visually distinct from the body; body rendered with `marked` and sanitized with `dompurify` before `{@html}` insertion; placeholder state for unavailable records; scrollable/responsive for very large records (spec Edge Cases).
- [X] T029 [US2] Create the detail route `frontend/src/routes/tasks/[taskId]/+page.svelte`: read `taskId` from route params, fetch via `getTaskRecord`, render `TaskRecordView`; add `frontend/src/routes/tasks/[taskId]/page.test.ts` covering route-level load and placeholder handling.
- [X] T030 [US2] Repoint the Details action in `frontend/src/lib/components/TaskCard.svelte` to the internal route `/tasks/{taskId}` (built from `taskId`, not from `taskLink` — the board API's `taskLink` field stays pointed at the JSON endpoint, research.md Decision 7); update `frontend/src/lib/components/TaskCard.svelte.test.ts` accordingly.

**Checkpoint**: US2 complete and independently verifiable via quickstart §3 (with manual refresh; live updates arrive with US3).

---

## Phase 5: User Story 3 — Task Detail View Updates Automatically (Priority: P3)

**Goal**: A Hub-side debounced `TasksDir` watcher publishes `taskRecordChanged` on the existing SignalR lifecycle hub; an open detail view refetches on events for its task and resynchronizes on reconnect — change visible ≤ 5 s (SC-005), never torn (FR-011).

**Independent Test**: quickstart §4 — with a detail view open, append to `<data>/wiki/tasks/<taskId>.md`: rendered view updates within 5 s without reload; restarting the Hub degrades then recovers the connection indicator and resynchronizes content.

### Tests for User Story 3 (write first — they MUST fail before implementation)

- [X] T031 [P] [US3] Create `backend/tests/Grimoire.IntegrationTests/TaskRecordWatcherTests.cs` (hermetic, temp `TasksDir`, SignalR test client): atomic-rename write of a record → exactly one `taskRecordChanged {eventId, taskId, changedAt}` per contracts/task-record-changed-event.md within the freshness budget; rapid successive writes within 300 ms coalesce to one event; `.*.tmp` temp files produce no events; watcher IO failure triggers self-restart and events resume.
- [X] T032 [P] [US3] Extend `frontend/src/lib/services/ingestLifecycleClient.test.ts` and `frontend/src/routes/tasks/[taskId]/page.test.ts`: `onTaskRecordChanged` delivers events; the detail view refetches the record on an event for its own `taskId` (and only its own), refetches unconditionally on SignalR reconnect (FR-010), and surfaces staleness via the existing `ConnectionState` while disconnected.

### Implementation for User Story 3

- [X] T033 [US3] Add the `taskRecordChanged` event shape (`eventId`, `taskId`, `changedAt` per data-model.md) to `backend/src/Grimoire.Hub/Realtime/RealtimeLifecycleEvent.cs` and a publish method to `backend/src/Grimoire.Hub/Realtime/IngestLifecyclePublisher.cs` following the established ADR-008 event conventions on `IngestLifecycleHub`.
- [X] T034 [US3] Create `backend/src/Grimoire.Hub/Realtime/TaskRecordWatcher.cs` (hosted background service): `FileSystemWatcher` on `ResolvedGrimoirePaths` `TasksDir` (ADR-009 — no path re-derivation), per-`taskId` 300 ms debounce, ignore `.*.tmp` files, publish via `IngestLifecyclePublisher`, self-restart on watcher failure; observe-only — the watcher never writes files (ADR-002/003).
- [X] T035 [US3] Register `TaskRecordWatcher` as a hosted service in `backend/src/Grimoire.Hub/Program.cs`.
- [X] T036 [US3] Instrument the watcher/publish path (implementation; verification tests are Phase 6): `task_record.change_published` INFO log event (`task_id`, `event_id`, `changed_at`) and `task_record.watch_failed` WARN log event (`path`, `reason`) in `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionLogEvents.cs` (or a sibling `Realtime` log-events class if one is established); counter `hub.task_record_change_events_total` in `backend/src/Grimoire.Hub/HubMetrics.cs`; root span `hub.task_record.publish_change` (watcher-initiated, attributes `task_id`, `event_id`) in `backend/src/Grimoire.Hub/HubTracing.cs`; log and counter emitted within the span context.
- [X] T037 [P] [US3] Add the `TaskRecordChangedEvent` type to `frontend/src/lib/types.ts` and an `onTaskRecordChanged()` subscription to `frontend/src/lib/services/ingestLifecycleClient.ts` (dedupe by `eventId`, consistent with existing lifecycle handlers).
- [X] T038 [US3] Wire live updates into `frontend/src/routes/tasks/[taskId]/+page.svelte` / `TaskRecordView.svelte`: subscribe on mount, refetch `getTaskRecord` on events matching the route's `taskId`, refetch unconditionally on reconnect, reuse `ConnectionState`/`ConnectionStatusIndicator` for the staleness indication, unsubscribe on destroy.

**Checkpoint**: All three stories independently functional (quickstart §1–§4).

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Observability contract verification and DoD gates (Constitution Principles III & IV — these tests require the production code from Phases 4–5 and therefore MUST NOT run earlier).

### Observability contract tests (deterministic, in-memory OTel exporter — ADR-005)

- [ ] T039 [P] Create `backend/tests/Grimoire.IntegrationTests/TaskRecordLogEventTests.cs` validating every plan.md Structured Log Events row: `task_record.served` (INFO; `task_id`, `outcome`, `content_length`) for each trigger outcome ok/missing/unparseable; `task_record.change_published` (INFO; `task_id`, `event_id`, `changed_at`) on a debounced publish; `task_record.watch_failed` (WARN; `path`, `reason`) on a simulated watcher IO failure.
- [ ] T040 [P] Create `backend/tests/Grimoire.IntegrationTests/TaskRecordTraceTests.cs` validating every plan.md Distributed Trace Spans row: `hub.task_record.serve` exists with the ASP.NET Core request span as parent and attributes `task_id`/`outcome`; `hub.task_record.publish_change` exists as a watcher-initiated root span with attributes `task_id`/`event_id`; and the corresponding log events/metrics correlate to their span via shared `task_id` (Principle IV span-context requirement).
- [ ] T041 [P] Create `backend/tests/Grimoire.IntegrationTests/TaskRecordMetricsTests.cs`: `hub.task_record_reads_total` increments with the correct `outcome` label per read path; `hub.task_record_change_events_total` increments once per debounced publish.
- [ ] T042 CI enforcement for the logging and trace contracts: confirm `.github/workflows/ci.yml` executes T039–T041 and the Phase 0 arch rules in the standard PR pipeline (they live in `backend/tests/Grimoire.IntegrationTests` and `backend/tests/Grimoire.ArchTests`, whose `dotnet test` steps and the frontend `npm run test` step already run on PRs) — verify no test filter excludes them, run the pipeline once on the feature branch, and record the green run in the task commit.

### Final gates

- [ ] T043 Run the full quickstart.md validation (§1 conformance + probe replay, §2 offline regression, §3 detail view, §4 live update, §5 observability via Aspire dashboard) and fix anything that fails.
- [ ] T044 [P] Sweep for leftovers: no probe classes anywhere in `backend/` (spec Edge Cases), no remaining references to the old namespaces (`Grimoire.Hub.Conversion.MarkItDownConverter`, `Grimoire.Hub.Conversion.UrlContentFetcher`, `Grimoire.IngestAgent.AgentCore.AnthropicModelClient` outside `Adapters`), and `docs/adr/ADR-010-hexagonal-ports-adapter-namespaces.md` Verification section reflects the executed probes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0** → blocks everything (constitutional order: rules before feature code).
- **Phase 1 (T003)** → baseline before any restructuring; blocks Phase 3.
- **Phase 2** → empty, no gate.
- **Phase 3 (US1)** → blocks US2/US3 only through the namespaces it establishes; complete it first (it is also the P1 MVP). T004–T006 → T007–T010 → T011–T013 → T014–T016 → T017 → T018 → T019.
- **Phase 4 (US2)** → tests T020–T021 first; backend T022 → T023 → T024; frontend T025–T026 → T027 → T028 → T029 → T030. Independent of US3.
- **Phase 5 (US3)** → tests T031–T032 first; requires US2's endpoint/route for the refetch path. T033 → T034 → T035 → T036; T037 → T038.
- **Phase 6** → requires Phases 4–5 (observability tests need the production code to exist).

### User Story Dependencies

- **US1 (P1)**: only Phase 0 + baseline. Fully independent.
- **US2 (P2)**: builds on US1's namespaces (`IngestSubmission` read model/endpoint placement); independently testable via quickstart §3.
- **US3 (P3)**: builds on US2 (detail view is the event consumer); independently testable via quickstart §4.

### Parallel Opportunities

- **US1**: T005 ∥ T006 (after T004 starts, different files); T008 ∥ T009 ∥ T010 (independent moves; T007 first touches AgentDispatch alone); T014 ∥ T015.
- **US2**: T020 ∥ T021 (backend/frontend tests); T025 ∥ T026 while T022 proceeds.
- **US3**: T031 ∥ T032; T037 ∥ backend chain T033–T036.
- **Phase 6**: T039 ∥ T040 ∥ T041; T044 ∥ T043 after T042.

### Parallel Example: User Story 1

```bash
# After T004–T006 (ports) are merged, run the three adapter moves in parallel:
Task: "Move MarkItDownConverter/MarkItDownOptions to IngestSubmission/Adapters/MarkItDown (T008)"
Task: "Move UrlContentFetcher to IngestSubmission/Adapters/HttpFetch (T009)"
Task: "Move AnthropicModelClient to AgentCore/Adapters/Anthropic (T010)"

# Then create both new fakes in parallel:
Task: "FakeMarkdownConverter in tests/Grimoire.IntegrationTests/Fakes (T014)"
Task: "FakeUrlContentFetcher in tests/Grimoire.IntegrationTests/Fakes (T015)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 0 (T001–T002): rules exist; C1 proven; C2–C5 documented Red.
2. T003: baseline.
3. Phase 3 (T004–T019): restructure → arch suite Green → probes → offline regression gate.
4. **STOP and VALIDATE**: quickstart §1–§2. This alone unblocks all future features (spec US1 "Why this priority") and is shippable.

### Incremental Delivery

1. MVP (above) → merge-worthy increment 1.
2. US2 (T020–T030) → rendered detail view with manual refresh → validate quickstart §3 → increment 2.
3. US3 (T031–T038) → live updates → validate quickstart §4 → increment 3.
4. Phase 6 (T039–T044) → observability contracts + DoD gates → feature DONE per constitution Definition of Done.

---

## Notes

- Zero behavioral change is a hard constraint for US1 (SC-003): moves and interface extractions only; any test assertion change is a red flag.
- Backend code MUST NOT template, rewrite, or classify record *content* (Principle V) — the read model parses frontmatter and strips it, nothing more.
- All new paths resolve via `ResolvedGrimoirePaths` (ADR-009); the watcher observes and never writes (ADR-002/003).
- Probes (Phase 0 / T018) are never committed as surviving files; each probe's Red run is documented in a commit message.
- Commit after each task or logical group; move commits stay move-only for `git blame` mitigation (ADR-010).
