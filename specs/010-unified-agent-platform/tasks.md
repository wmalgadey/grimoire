# Tasks: Unified Agent Platform & Naming Convention

**Input**: Design documents from `/specs/010-unified-agent-platform/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md, ADR-013 (accepted)

**Tests**: Required — the constitution mandates Red/Green-probed structural tests for the
new ADR-013 rules (N1, D1, D2) and for every existing rule whose anchor moves (FR-010).
This feature declares **no agent-judgment success criteria** and **no new observability
rows** (plan.md ## Observability), so per the plan's derivation rule no new
logging/trace-contract or evaluation tasks are generated; instead, explicit
behavior-preservation regression tasks (US3) and the final-phase completeness audit
cover the frozen contracts.

**Organization**: Tasks are grouped by user story (spec.md priorities P1–P3). US3
(behavior preservation) is a constraint on how US1/US2 are done; its phase verifies, it
does not build.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- File paths are exact, relative to repository root

## Path Conventions

Existing web-app split: `backend/src/`, `backend/tests/`; solution file
`backend/Grimoire.slnx`. This feature adds three namespaces inside
`backend/src/Grimoire.AgentRuntime/` (`Telemetry/`, `Composition/`, `Host/`), one Hub
namespace folder (`backend/src/Grimoire.Hub/IngestDispatch/`), one convention document
(`docs/conventions/agent-artifact-naming.md`), and three arch-rule files. No project is
added or removed; frontend, instruction files (`data/agents/`), and recordings
(`data/evals/recordings/`) are untouched.

**Legacy-baseline ratchet (used by Phase 0)**: the codebase currently *contains* the
violations the new rules exist to forbid (duplicated host scaffolds for D1/D2, the R5
misnamed artifacts for N1). Each Phase 0 rule therefore ships with an explicit,
in-test **legacy-violation baseline list** naming exactly today's known violators, so
the rule is green in CI from day one for everything else and any *new* violation fails
immediately (proven by the probe). The story phases empty these baselines as they
consolidate/rename; Phase 6 asserts every baseline is empty and deletes the mechanism.
A baseline entry may only ever be removed, never added — adding one is a review-reject.

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Prove the ADR-013 rules (N1 naming, D1 telemetry-bootstrap containment,
D2 model-adapter composition containment) are live *before* any feature code exists.
This phase is first, non-negotiable, and blocks everything else.

**⚠️ NON-NEGOTIABLE**: No feature implementation can begin until Phase 0 is complete.

- [X] T001 Write `backend/tests/Grimoire.ArchTests/AgentArtifactNamingRuleTests.cs`
  (rule **N1**, ADR-013): Mono.Cecil scan (same idiom as
  `HubAgentDispatchBoundaryRuleTests.cs`) over the shared assemblies
  `Grimoire.IntegrationTests`, `Grimoire.AgentEvals`, `Grimoire.ArchTests`, and
  `Grimoire.EvalRunner` scenario types — for each top-level type, compute the set of
  agent-owned assemblies/namespaces it references (`Grimoire.IngestAgent[.*]`,
  `Grimoire.QueryAgent[.*]`, plus the Hub ownership map below); a type referencing
  exactly **one** agent must carry that agent's token (`Ingest`/`Query`) in its name.
  Second assertion: an explicit `Grimoire.Hub` namespace-ownership map (ingest-owned:
  `IngestSubmission`, `IngestDispatch`, `IngestTaskArtifact`; query-owned:
  `QueryDispatch`, `QuerySubmission`, `QueryRunArtifact`; cross-agent: `AgentDispatch`,
  `Realtime`, `Runtime`, `ContentRoot`, `OperationalState`, `Conversion`) — agent-owned
  types must not live in unprefixed Hub namespaces and vice versa. Include (a) an
  in-code curated **exemption fixture** (initially: the research.md R5 cross-agent list
  — `ReplayContractTests`, `StalenessTests`, `CaptureHygieneTests`,
  `EvalProviderResolverTests`, `TimeoutEnforcingModelClientTests`,
  `SyntheticRecordings`, `LocalEnvFileTests`, `PathTraversalTests`,
  `PolicyMisconfigurationTests`, `TraceContextPropagationTests`,
  `HubRequestTracingTests`, shared `Fakes/`) and (b) a **legacy-rename baseline**
  seeded with the complete R5 violation inventory (`ReplayEvalTests`,
  `ScenarioDefinitions`, `GuardedWriteBoundaryRuleTests`, `ObservabilityLogTests`,
  `ObservabilityMetricsTests`, `ObservabilityTraceTests`, `AgentRunLifecycleTests`,
  `AgentTaskArtifactTests`, `InstructionContextTests`, `InstructionLoadFailureTests`,
  `UserPromptTests`, `RunQueueTests`, `RunSupervisionTests`,
  `RunActivityRealtimeTests`, `FailureAndReconciliationTests`, `ConvertStepTests`,
  `SourceArtifactPersistenceTests`, `SubmissionPromptApiTests`,
  `Grimoire.Hub.AgentDispatch.IngestRunCoordinator`,
  `Grimoire.Hub.AgentDispatch.IngestAgentRequest`,
  `Grimoire.Hub.AgentDispatch.QueryAgentRequest`, `Grimoire.Hub.Submission.*`,
  `Grimoire.Hub.TaskArtifact.*`, `AgentCliOptions`,
  `Grimoire.IngestAgent.AgentCore.*`) so the rule is green today and ratchets. The
  doc↔fixture mirror assertion against `docs/conventions/agent-artifact-naming.md` is
  deferred to T042 (the document is a US2 deliverable).
- [X] T002 Red/Green probe for T001 (FR-007/SC-002): add a temporary
  `public class MisnamedEvalTests` referencing only `Grimoire.IngestAgent` types to
  `backend/tests/Grimoire.IntegrationTests/`, run
  `dotnet test backend/tests/Grimoire.ArchTests` — N1 MUST fail; delete the class, run
  again — MUST pass. Commit message documents the probe result per the constitution's
  Phase 0 requirement. Probe result: `MisnamedEvalTests` (field of type
  `Grimoire.IngestAgent.AgentCliOptions`) turned N1 RED naming exactly that type;
  after deletion 40/40 ArchTests GREEN.
- [X] T003 Write `backend/tests/Grimoire.ArchTests/AgentHostTelemetryContainmentRuleTests.cs`
  (rule **D1**, ADR-013): in agent host assemblies (`Grimoire.IngestAgent`,
  `Grimoire.QueryAgent`, and by naming pattern any future `Grimoire.*Agent`
  executable), OpenTelemetry SDK provider-construction APIs
  (`Sdk.CreateTracerProviderBuilder`, `Sdk.CreateMeterProviderBuilder`, OTel
  `LoggerFactory` wiring) are forbidden — permitted only in
  `Grimoire.AgentRuntime.Telemetry`. Legacy baseline (removed by T024/T027):
  `Grimoire.IngestAgent/TelemetryBootstrap.cs`,
  `Grimoire.QueryAgent/QueryAgentTelemetryBootstrap.cs`.
- [X] T004 Red/Green probe for T003 (SC-001): paste a temporary private
  `Sdk.CreateTracerProviderBuilder()` call into a scratch type in
  `backend/src/Grimoire.QueryAgent/`, run the ArchTests — D1 MUST fail; remove, run
  again — MUST pass. Commit message documents the probe result. Probe result:
  scratch `TelemetryProbe.Build` calling `Sdk.CreateTracerProviderBuilder()` turned
  D1 RED naming exactly that call site; after deletion 40/40 ArchTests GREEN.
- [X] T005 Write `backend/tests/Grimoire.ArchTests/AgentHostModelCompositionContainmentRuleTests.cs`
  (rule **D2**, ADR-013): agent host assemblies must not construct
  `AnthropicModelClient`/`ReplayModelClient`/`TurnCaptureModelClient` directly (IL scan
  for constructor references); the only permitted construction site is
  `Grimoire.AgentRuntime.Composition.ModelClientFactory` (invoked from each host's
  composition root; ADR-012 selection semantics and env-var contract unchanged). Legacy
  baseline (removed by T023/T026): `CreateModelClient` in
  `backend/src/Grimoire.IngestAgent/Program.cs` and
  `backend/src/Grimoire.QueryAgent/Program.cs`.
- [X] T006 Red/Green probe for T005 (SC-001): add a temporary direct
  `new AnthropicModelClient(...)` construction in a scratch class in
  `backend/src/Grimoire.IngestAgent/`, run the ArchTests — D2 MUST fail; remove, run
  again — MUST pass. Commit message documents the probe result. Probe result: scratch
  `ModelClientProbe.Create` doing `new AnthropicModelClient(...)` turned D2 RED
  naming exactly that construction; after deletion 40/40 ArchTests GREEN.

**Definition of Done**:

- [X] All three rules (T001, T003, T005) written and committed, green in CI with their
  seeded legacy baselines and no other violations (seeding note: T001's report-mode
  run surfaced four artifacts beyond the R5 list — `QueryPriorTurn` in
  `Grimoire.Hub.AgentDispatch` (baselined, moves in T036),
  `DispatchPathArgumentsTests`/`RepoLessStartupTests` (baselined, settled by T041),
  and `QueryConcurrencyIndependenceTests`/`HexagonalPortsAdapterRuleTests`/
  `RuntimePathsBoundaryRuleTests` classified cross-agent in the exemption fixture)
- [X] All three Red/Green probes (T002, T004, T006) completed with commit messages
  documenting the probe result

**Checkpoint**: ADR-013's structural boundaries are guarded and ratcheting. Feature
code may now begin.

---

## Phase 1: Setup (Baseline & Inventory)

**Purpose**: Freeze the pre-consolidation baseline that US3's preservation guarantee is
measured against, and finalize the rename inventory the convention document needs.

- [ ] T007 Verify the branch-point baseline: `dotnet build backend/Grimoire.slnx` and
  `dotnet test` for `backend/tests/Grimoire.ArchTests`,
  `backend/tests/Grimoire.Domain.UnitTests`, `backend/tests/Grimoire.IntegrationTests`,
  and `backend/tests/Grimoire.AgentEvals` — all green, **zero skipped** (ADR-012
  zero-skip gate). Record the passing test counts per project (in the task-completion
  note / commit message); this is the SC-003/FR-009 reference point for "full
  pre-existing suite passes with no weakened assertions".
- [ ] T008 [P] Finalize the authoritative N1 violation inventory: run T001's rule in
  report mode (or temporarily list the baseline contents) and cross-check against
  research.md R5, capturing any single-agent-owned unprefixed artifact R5 missed
  (candidates seen in survey: `OperationalStateAndDispatchTests`,
  `KanbanBoardApiTests`, `GovernanceIdentityTests`, `ReplayAdapterTests`,
  `UrlContentFetcherTests`, `PathConfiguration/*` test files,
  `IngestSubmissionPipelineFixture` naming). Output: the confirmed old→new mapping
  (input to T029's convention document) plus the confirmed cross-agent list (input to
  the exemption fixture). Do not rename anything yet.

**Checkpoint**: Baseline recorded, inventory authoritative. Foundational work can begin.

---

## Phase 2: Foundational (Platform Components — Built, Not Yet Consumed)

**Purpose**: Build the new `Grimoire.AgentRuntime` platform components as pure
additions. Nothing consumes them until Phase 3, so every existing test stays green
throughout this phase. Per Constitution Principle II these consolidation targets get no
dedicated unit tests — the existing integration/replay suites cover them implicitly
the moment the hosts adopt them (Phase 3), which is the actual verification.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T009 [P] Implement `backend/src/Grimoire.AgentRuntime/Host/AgentProfile.cs`
  (namespace `Grimoire.AgentRuntime.Host`): plain record per data-model.md — `AgentName`,
  `ServiceName`, `ActivitySourceName`/`MeterName`, `RunSpanName`,
  `CorrelationAttribute`, `ToolRegistry`, `RequiredInstructionDocuments`,
  `ModelEnvVarNames` (model + base-url env-var name pair). In-memory only, never
  serialized; no behavior, no agent-conditional logic.
- [ ] T010 [P] Implement `backend/src/Grimoire.AgentRuntime/Telemetry/AgentTelemetryBootstrap.cs`
  (namespace `Grimoire.AgentRuntime.Telemetry`): OTel tracer/meter/logger provider
  construction + OTLP export, parameterized by the profile's
  service/source/meter names — must reproduce the provider wiring of
  `backend/src/Grimoire.IngestAgent/TelemetryBootstrap.cs` and
  `backend/src/Grimoire.QueryAgent/QueryAgentTelemetryBootstrap.cs` exactly (same
  resources, sources, meters, exporter setup); the frozen identities
  (`Grimoire.IngestAgent`, `Grimoire.QueryAgent`) arrive as inputs, never as literals
  in the platform (ADR-005/ADR-013).
- [ ] T011 [P] Implement `backend/src/Grimoire.AgentRuntime/Telemetry/AgentTracing.cs`:
  ActivitySource holder + run-span start helper, parameterized by source name, run-span
  name, and correlation-attribute name (`task_id`/`turn_id`) — consolidates the 28/27-line
  scaffolds of `IngestAgentTracing.cs`/`QueryAgentTracing.cs`; span names are inputs,
  unchanged (research.md R2).
- [ ] T012 [P] Implement `backend/src/Grimoire.AgentRuntime/Composition/ModelClientFactory.cs`
  (namespace `Grimoire.AgentRuntime.Composition`): the ADR-012 replay/capture/live
  selection (`GRIMOIRE_MODEL_REPLAY_PATH`/`GRIMOIRE_MODEL_CAPTURE_PATH`, fail-fast when
  both set) implemented once, taking the per-agent model/base-url env-var names from
  the profile (ADR-004 scoping: Ingest defaults vs
  `GRIMOIRE_QUERY_MODEL`/`GRIMOIRE_QUERY_BASE_URL`). Port the union of both existing
  `CreateModelClient` implementations byte-compatibly (behavioral reference:
  `backend/src/Grimoire.IngestAgent/Program.cs` and
  `backend/src/Grimoire.QueryAgent/Program.cs`).
- [ ] T013 [P] Implement `backend/src/Grimoire.AgentRuntime/Composition/ErrorSanitizer.cs`:
  single implementation of the credential-bearing error-text sanitization currently
  duplicated as `SanitizeErrorText` in both hosts' `Program.cs` (identical output for
  identical input — terminal `failed` event text is observable behavior, FR-008).
- [ ] T014 [P] Implement `backend/src/Grimoire.AgentRuntime/Composition/AgentArgumentReader.cs`:
  the shared `--key value` CLI parsing scaffold (required/optional helpers, heartbeat
  default) currently duplicated as `ParseArgs` in both hosts; each host keeps only its
  own option record (`AgentCliOptions`→`IngestCliOptions` in US2, `QueryCliOptions`).
  Unknown-argument/missing-argument error text unchanged (spawn contract, ADR-002).
- [ ] T015 Implement `backend/src/Grimoire.AgentRuntime/Host/AgentHost.cs`: the
  startup/shutdown template — fail-closed instruction + policy load (existing
  `SystemPromptLoader`/`PolicyLoader`, unchanged) → `started` event → heartbeat →
  `AgentLoop` → terminal event — consuming an `AgentProfile` (T009) plus intent hooks
  (delegates/interface) for per-agent behavior (Ingest: task-artifact lifecycle,
  ingest-log appending, source reading, rollback/all-denied handling, user-prompt
  resolution; Query: stdin conversation scaffold). The ADR-008/ADR-011 event
  sequencing must be byte-compatible; **no `if (agent == …)` branches anywhere in the
  platform** (FR-002). Depends on T009.

**Checkpoint**: The platform is complete and dormant; all suites still green.
User story implementation can now begin.

---

## Phase 3: User Story 1 — Two agents, one platform (Priority: P1) 🎯 MVP

**Goal**: Ingest and Query run on the shared platform; every shared concern exists
exactly once; per-agent code shrinks to profile + intent handling; D1/D2 go green with
empty baselines and are re-probed live.

**Independent Test**: Enumerate the per-agent code of both hosts and verify only
profile artifacts + intent-specific handling remain; grep/arch-scan proves no shared
concern exists twice; full suite green after each host switch.

### Implementation for User Story 1

- [ ] T016 [US1] Create `backend/src/Grimoire.IngestAgent/IngestToolRegistry.cs`:
  explicit profile tool set registering exactly today's Ingest tools (`list_files`,
  `read_file`, `write_file` — unchanged schemas) against the shared
  `GuardedToolExecutor`, replacing any inline/implicit registration in `Program.cs`
  (FR-004: capabilities == declaration).
- [ ] T017 [US1] Switch `backend/src/Grimoire.IngestAgent/Program.cs` onto the
  platform: declare the Ingest `AgentProfile` (ServiceName `Grimoire.IngestAgent`,
  run span `ingest_agent.run`, correlation `task_id`, default model env-var names,
  `IngestToolRegistry`, required documents: system prompt + default user prompt), wire
  the Ingest intent hooks, and replace `ParseArgs`/`CreateModelClient`/
  `SanitizeErrorText` with `AgentArgumentReader`/`ModelClientFactory`/`ErrorSanitizer`
  and the inline startup sequencing with `AgentHost` (T015). CLI surface, NDJSON event
  sequence, exit codes, and artifact behavior byte-identical (FR-008; ADR-002/008
  spawn/event contracts).
- [ ] T018 [US1] Delete `backend/src/Grimoire.IngestAgent/TelemetryBootstrap.cs` and
  make `backend/src/Grimoire.IngestAgent/IngestAgentTracing.cs` delegate to
  `AgentTracing` (T011) with its frozen identities; bootstrap via
  `AgentTelemetryBootstrap` (T010). Remove the Ingest entries from D1's legacy
  baseline in `AgentHostTelemetryContainmentRuleTests.cs` and the Ingest entry from
  D2's baseline in `AgentHostModelCompositionContainmentRuleTests.cs`.
- [ ] T019 [US1] Ingest preservation gate: run
  `dotnet test backend/tests/Grimoire.IntegrationTests` and
  `backend/tests/Grimoire.AgentEvals` (ingest replay suite) — all green against
  unchanged recordings; any `stale`/`mismatch` replay failure is a defect in the
  consolidation (research.md R7), not an occasion to re-capture.
- [ ] T020 [US1] Switch `backend/src/Grimoire.QueryAgent/Program.cs` onto the
  platform: declare the Query `AgentProfile` (ServiceName `Grimoire.QueryAgent`, run
  span `query_agent.run`, correlation `turn_id`,
  `GRIMOIRE_QUERY_MODEL`/`GRIMOIRE_QUERY_BASE_URL`, existing `QueryToolRegistry`
  (`list_files`, `read_file` only), required documents: system prompt only), wire the
  stdin-conversation intent hook, and replace the duplicated
  `ParseArgs`/`CreateModelClient`/`SanitizeErrorText`/inline sequencing with the
  platform components, mirroring T017.
- [ ] T021 [US1] Delete `backend/src/Grimoire.QueryAgent/QueryAgentTelemetryBootstrap.cs`
  and make `backend/src/Grimoire.QueryAgent/QueryAgentTracing.cs` delegate to
  `AgentTracing`; remove the remaining Query entries from D1's and D2's legacy
  baselines — **both baselines are now empty**.
- [ ] T022 [US1] Query preservation gate: run
  `dotnet test backend/tests/Grimoire.IntegrationTests` (Query lifecycle
  log/metric/trace, streaming, interruption, guardrail tests) and the
  `QueryReplayEvalTests` replay suite — all green, assertions untouched.
- [ ] T023 [US1] Post-consolidation Red/Green re-probe of D1 and D2 in their final
  (empty-baseline) state, per spec US1 acceptance scenario 3: re-introduce a private
  `Sdk.CreateTracerProviderBuilder()` bootstrap in a host → D1 red → remove → green;
  re-introduce a direct `new AnthropicModelClient(...)` in a host composition root
  bypassing the factory → D2 red → remove → green. Commit messages document both
  probe results.
- [ ] T024 [US1] Re-probe `backend/tests/Grimoire.ArchTests/QueryAgentGuardedWriteBoundaryRuleTests.cs`
  after the Query host restructuring (FR-004/FR-010, plan Test Strategy SC-004): the
  rule's scan must cover the new `AgentHost`-routed tool-dispatch path; add a
  deliberate `File.WriteAllText(...)` call in a `Grimoire.QueryAgent` scratch class →
  red → remove → green. Update the rule's namespace anchors only as far as the
  restructuring moved them (the rule moves with the boundary, it is not weakened).
- [ ] T025 [P] [US1] Profile-fidelity test (SC-004, plan Test Strategy): new hermetic
  integration test `backend/tests/Grimoire.IntegrationTests/AgentProfileFidelityTests.cs`
  (cross-agent — covers both agents, stays unprefixed by the convention) asserting (a)
  each host's registered tool set is exactly its profile declaration (Ingest:
  `list_files`/`read_file`/`write_file`; Query: `list_files`/`read_file`), and (b) a
  `FakeModelClient` scripted to request an out-of-profile tool is denied at the guarded
  tool boundary and the run continues.
- [ ] T026 [US1] US1 acceptance-scenario-1 verification: enumerate the per-agent code
  of `backend/src/Grimoire.IngestAgent/` and `backend/src/Grimoire.QueryAgent/` and
  confirm each contains only: composition root (`Program.cs`), CLI option record, tool
  registry, instrumentation adapters + frozen metric/log-event/tracing identity
  definitions, and intent-specific artifact handling (`TaskArtifact/`, `IngestLog/`,
  `Source/` vs conversation scaffold). Confirm each R2 concern exists exactly once:
  `grep -rn "CreateModelClient\|SanitizeErrorText\|ParseArgs\|CreateTracerProviderBuilder" backend/src/Grimoire.IngestAgent backend/src/Grimoire.QueryAgent`
  returns nothing. Record the enumeration in the PR description (input to T048's
  audit).

**Checkpoint**: One platform, two thin hosts, D1/D2 green with empty baselines and
proven live, full suite green. US1 is independently complete.

---

## Phase 4: User Story 2 — A file's name tells you whose it is (Priority: P2)

**Goal**: The convention exists as a versioned document with the complete rename map;
every R5 violator is renamed/moved; the ambiguous artifacts are explicitly classified;
N1 is green with an empty baseline and proven live.

**Independent Test**: N1 passes with an empty legacy baseline; the convention document
exists and mirrors N1's exemption fixture; a deliberately misnamed artifact fails the
build (probe); replay recordings and scenario ids are untouched.

### Implementation for User Story 2

- [ ] T027 [US2] Create `docs/conventions/agent-artifact-naming.md` (FR-005): (a) the
  rule — agent-specific code artifacts carry their agent's name, unprefixed names are
  reserved for genuinely cross-agent artifacts; (b) rationale (ownership legibility
  ahead of agent three); (c) the explicit cross-agent definition (serves ≥2 agents or
  the platform/harness itself ⇒ unprefixed — a fixture shared by ingest and query
  tests stays unprefixed, per the spec edge case); (d) the exemption list with a
  justification per entry; (e) the complete old→new rename map from T008's confirmed
  inventory (headline: `ReplayEvalTests` → `IngestReplayEvalTests`), which parallel
  branches (011/012/013) use to rebase mechanically.
- [ ] T028 [P] [US2] Classification: `backend/tests/Grimoire.IntegrationTests/CredentialScopingTests.cs`
  — run N1's reference analysis; if it exercises both agents' spawn/credential paths
  (survey suggests it references Query path options), classify cross-agent: keep the
  name, add a justified exemption entry to `docs/conventions/agent-artifact-naming.md`
  and the N1 fixture. If it in fact covers only the Ingest spawn, rename to
  `IngestCredentialScopingTests.cs` and add it to the rename map. Record the decision
  and evidence in the convention document either way (ADR-013 directs tasks to settle
  this).
- [ ] T029 [P] [US2] Classification: `TaskRecordApiTests.cs`,
  `TaskRecordLogEventTests.cs`, `TaskRecordMetricsTests.cs`,
  `TaskRecordTraceTests.cs`, `TaskRecordWatcherTests.cs` in
  `backend/tests/Grimoire.IntegrationTests/` — Task Records are the ingest Kanban
  read model, so N1's analysis is expected to classify them Ingest-owned: rename to
  `IngestTaskRecord*Tests.cs` (classes likewise) and add to the rename map; if the
  analysis instead shows genuine cross-agent references, exempt with justification.
  Decision recorded in the convention document.
- [ ] T030 [P] [US2] Classification: `ScenarioDefinition` vs `ScenarioDefinitions` in
  `backend/src/Grimoire.EvalRunner/Scenarios/ScenarioDefinitions.cs` — the
  `ScenarioDefinition` record is consumed by both `ScenarioDefinitions` (Ingest) and
  `QueryScenarioDefinitions` (Query): classify it cross-agent, extract it to its own
  file `backend/src/Grimoire.EvalRunner/Scenarios/ScenarioDefinition.cs`, keep it
  unprefixed, and document the justification. The Ingest-only `ScenarioDefinitions`
  static class is renamed by T032. Decision recorded in the convention document.
- [ ] T031 [P] [US2] Rename `backend/tests/Grimoire.AgentEvals/ReplayEvalTests.cs` →
  `IngestReplayEvalTests.cs` (class `ReplayEvalTests` → `IngestReplayEvalTests`) via
  `git mv`, assertions untouched — the headline FR-006 rename; sibling
  `QueryReplayEvalTests.cs` already conforms. The AgentEvals infra tests
  (`ReplayContractTests`, `StalenessTests`, `CaptureHygieneTests`, etc.) stay
  unprefixed per the exemption list.
- [ ] T032 [US2] Rename `ScenarioDefinitions` → `IngestScenarioDefinitions`
  (file `backend/src/Grimoire.EvalRunner/Scenarios/ScenarioDefinitions.cs` →
  `IngestScenarioDefinitions.cs`, class likewise) via `git mv`; update references in
  `backend/src/Grimoire.EvalRunner/Program.cs` and the AgentEvals suites. Scenario
  **ids/slugs and `data/evals/recordings/<scenario>/` directory names stay unchanged**
  (research.md R5/R7) — verified by T035.
- [ ] T033 [US2] Rename `backend/tests/Grimoire.ArchTests/GuardedWriteBoundaryRuleTests.cs`
  → `IngestAgentGuardedWriteBoundaryRuleTests.cs` (class likewise) via `git mv`
  (sibling `QueryAgentGuardedWriteBoundaryRuleTests.cs` already conforms;
  shared-runtime coverage remains inside both rules), update its namespace anchors to
  the post-US1 Ingest host structure, and **re-probe** (FR-010): deliberate
  out-of-allowed-namespace write call → red → remove → green.
- [ ] T034 [P] [US2] Batch-rename the Ingest-owned integration tests in
  `backend/tests/Grimoire.IntegrationTests/` via `git mv`, classes and file names,
  assertions untouched: `ObservabilityLogTests` → `IngestObservabilityLogTests`,
  `ObservabilityMetricsTests` → `IngestObservabilityMetricsTests`,
  `ObservabilityTraceTests` → `IngestObservabilityTraceTests`,
  `AgentRunLifecycleTests` → `IngestRunLifecycleTests`, `AgentTaskArtifactTests` →
  `IngestTaskArtifactTests`, `InstructionContextTests` →
  `IngestInstructionContextTests`, `InstructionLoadFailureTests` →
  `IngestInstructionLoadFailureTests`, `UserPromptTests` → `IngestUserPromptTests`,
  `RunQueueTests` → `IngestRunQueueTests`, `RunSupervisionTests` →
  `IngestRunSupervisionTests`, `RunActivityRealtimeTests` →
  `IngestRunActivityRealtimeTests`, `FailureAndReconciliationTests` →
  `IngestFailureAndReconciliationTests`, `ConvertStepTests` → `IngestConvertStepTests`,
  `SourceArtifactPersistenceTests` → `IngestSourceArtifactPersistenceTests`,
  `SubmissionPromptApiTests` → `IngestSubmissionPromptApiTests`.
- [ ] T035 [US2] Recording/replay rename-safety verification (research.md R7): after
  T031/T032, run `dotnet test backend/tests/Grimoire.AgentEvals` — green against the
  **unchanged** recordings; `git status --porcelain data/evals/recordings/` is empty;
  grep confirms scenario ids/slugs and recording paths are byte-identical to `main`.
  Any staleness-fingerprint drift is a defect in the change set, never an occasion to
  re-capture.
- [ ] T036 [US2] Hub namespace move (owner-confirmed scope): create
  `backend/src/Grimoire.Hub/IngestDispatch/` and move `IngestRunCoordinator.cs` and
  `IngestAgentRequest.cs` there from `backend/src/Grimoire.Hub/AgentDispatch/`
  (namespace `Grimoire.Hub.IngestDispatch`) via `git mv`; move `QueryAgentRequest.cs`
  from `AgentDispatch/` into `backend/src/Grimoire.Hub/QueryDispatch/` (namespace
  `Grimoire.Hub.QueryDispatch`). `IAgentProcessLauncher.cs`, `AgentRunEvent.cs`, and
  `Adapters/` **stay** in cross-agent `Grimoire.Hub.AgentDispatch` — the ADR-010/011
  port table and containment rules keep their anchor. Update all `using` references
  (Hub `Program.cs`, tests, fakes).
- [ ] T037 [P] [US2] Hub namespace merge: move `SubmissionService.cs` and
  `SubmitSourceOptions.cs` from `backend/src/Grimoire.Hub/Submission/` into
  `backend/src/Grimoire.Hub/IngestSubmission/` (namespace
  `Grimoire.Hub.IngestSubmission`) via `git mv`; delete the emptied `Submission/`
  folder; update references.
- [ ] T038 [P] [US2] Hub namespace rename: `backend/src/Grimoire.Hub/TaskArtifact/` →
  `backend/src/Grimoire.Hub/IngestTaskArtifact/` (namespace
  `Grimoire.Hub.IngestTaskArtifact`; `HubTaskArtifactDocument.cs`/
  `HubTaskArtifactWriter.cs` move with it) via `git mv`; update references. Persisted
  artifact locations/formats are untouched — this is a code-namespace move only
  (FR-008).
- [ ] T039 [P] [US2] Ingest host internal renames: `backend/src/Grimoire.IngestAgent/AgentCliOptions.cs`
  → `IngestCliOptions.cs` (record `AgentCliOptions` → `IngestCliOptions`); dissolve
  the `Grimoire.IngestAgent.AgentCore` namespace by moving
  `backend/src/Grimoire.IngestAgent/AgentCore/IngestAgentInstrumentation.cs` up into
  `backend/src/Grimoire.IngestAgent/` (namespace `Grimoire.IngestAgent`) and deleting
  the empty folder, via `git mv`.
- [ ] T040 [US2] Update every arch rule whose namespace/type anchors were moved by
  T036–T039 and re-probe each updated rule (FR-010 — the rule moves with the
  boundary, it is never dropped): survey and update
  `backend/tests/Grimoire.ArchTests/HubAgentDispatchBoundaryRuleTests.cs` (C4/C5 —
  launcher port/adapter anchors unchanged, coordinator references now
  `Grimoire.Hub.IngestDispatch`), `NonBlockingDispatchRuleTests.cs`,
  `RuntimePathsBoundaryRuleTests.cs`, `HexagonalPortsAdapterRuleTests.cs`, and
  `EvalRunnerReplayBoundaryTests.cs` (post-T032 type names). Each updated rule gets a
  fresh deliberate-violation → red → remove → green probe documented in the commit
  message.
- [ ] T041 [US2] Sweep the remaining N1 baseline: for every artifact still on T001's
  legacy list after T028–T039 (candidates from T008: `OperationalStateAndDispatchTests`,
  `KanbanBoardApiTests`, `GovernanceIdentityTests`, `ReplayAdapterTests`,
  `UrlContentFetcherTests`, `PathConfiguration/*`, `Fakes/IngestSubmissionPipelineFixture.cs`
  naming) either rename per the convention or classify cross-agent with a justified
  exemption entry — until the baseline list is empty. Every outcome lands in the
  convention document's map or exemption list.
- [ ] T042 [US2] Wire the doc↔fixture mirror assertion into
  `backend/tests/Grimoire.ArchTests/AgentArtifactNamingRuleTests.cs`: N1 parses the
  exemption list out of `docs/conventions/agent-artifact-naming.md` and fails on any
  drift between document and in-test fixture (data-model.md Naming Convention
  Document validation); delete the (now empty) legacy-rename baseline mechanism from
  N1 — from here on the rule enforces the convention outright.
- [ ] T043 [US2] Final FR-007/SC-002 Red/Green probe of N1 in its enforcing state:
  introduce a deliberately misnamed agent-specific artifact (e.g.
  `public class MisnamedEvalTests` referencing only `Grimoire.QueryAgent`) → build
  fails on N1 → remove → green. Commit message documents the probe; this is the spec
  US2 acceptance-scenario-3 evidence.

**Checkpoint**: Name identifies owner everywhere; the convention document and N1
enforce it with an empty baseline; recordings untouched. US2 is independently complete.

---

## Phase 5: User Story 3 — Nothing observable changes (Priority: P3)

**Goal**: Prove the consolidation is invisible: the full pre-existing suite passes
with no weakened assertions, artifacts/events are shape-identical, observability
identities and CI gates survived the renames.

**Independent Test**: quickstart.md steps 1–3 — full suite green with zero skips, the
`git diff main -- backend/tests` no-weakening audit is clean, and the preserved
observability contracts are still asserted in the standard PR pipeline.

### Verification for User Story 3

- [ ] T044 [US3] Full-suite preservation gate (SC-003, quickstart step 1):
  `dotnet build backend/Grimoire.slnx` then `dotnet test` for
  `backend/tests/Grimoire.ArchTests`, `backend/tests/Grimoire.Domain.UnitTests`,
  `backend/tests/Grimoire.IntegrationTests`, `backend/tests/Grimoire.AgentEvals` —
  everything green, **zero skipped**, replay suites (`IngestReplayEvalTests`,
  `QueryReplayEvalTests`) green against unchanged recordings. Compare passing-test
  counts against T007's baseline: count may only grow (new N1/D1/D2/profile-fidelity
  tests), never shrink.
- [ ] T045 [US3] No-weakening audit (FR-009, quickstart step 1): review
  `git diff main -- backend/tests` and confirm it contains only file/class renames,
  namespace/`using` updates, the new rule files
  (`AgentArtifactNamingRuleTests.cs`, `AgentHostTelemetryContainmentRuleTests.cs`,
  `AgentHostModelCompositionContainmentRuleTests.cs`,
  `AgentProfileFidelityTests.cs`), and anchor updates from T040 — **no assertion
  edited, weakened, or removed**. Record the audit result in the PR description
  (reviewers re-verify per the plan's Test Strategy).
- [ ] T046 [US3] Artifact/event shape identity (FR-008, spec US3 acceptance
  scenario 2): confirm the existing contract-pinning tests ran **unmodified** inside
  T044 — task-artifact schema, query-run-artifact schema, NDJSON run-event tests,
  SignalR payload tests — their green state is the structural-identity proof
  (research.md R7). Optionally corroborate via quickstart step 3's replay-driven
  manual runs of both agents (no credentials).
- [ ] T047 [US3] Preserved-observability regression (plan.md ## Observability
  derivation rule (a)): confirm every preserved-contract test class still executes in
  the standard PR pipeline after the renames — `IngestObservability{Log,Metrics,Trace}Tests`,
  `IngestLifecycleLogEventTests`/`IngestLifecycleTraceTests`/
  `IngestSubmission{LogEvent,Metrics,Trace}Tests`,
  `QueryLifecycle{LogEvent,Metrics,Trace}Tests`, `HubRequestTracingTests`,
  `EvalRunnerObservabilityTests`, `EvalObservabilityTests` — by verifying
  `.github/workflows/ci.yml` still runs `backend/tests/Grimoire.IntegrationTests` and
  `backend/tests/Grimoire.ArchTests` unfiltered (no class-name filter, category, or
  skip that a rename could silently orphan) and that each listed class appears in the
  CI test output. Frozen identities (service/meter/source names, span names/parents,
  metric/log-event names + mandatory fields) are asserted by these untouched tests —
  their green state is the identity check (ADR-005).
- [ ] T048 [US3] Capability preservation sign-off (FR-004/SC-004, spec US3 acceptance
  scenario 3): confirm `QueryReadOnlyGuardrailTests` and
  `QueryAgentGuardedWriteBoundaryRuleTests` are green post-consolidation (T024's
  re-probe evidence), the Query profile declares exactly `list_files`/`read_file`
  (T025's fidelity test), and the Ingest profile declares exactly today's three tools
  — no agent's capabilities widened or narrowed. Cross-reference T025/T024 results;
  record in the PR description.

**Checkpoint**: The consolidation is provably invisible. All three user stories are
complete.

---

## Phase 6: Polish & Completeness Audit

**Purpose**: Final DoD gates — the named completeness audit, CI enforcement of the new
rules, and quickstart validation.

- [ ] T049 **Completeness audit** (MANDATORY named final-phase task — Constitution
  Principle III/IV): cross-reference, in `specs/010-unified-agent-platform/tasks.md`
  completion notes or the PR description, (a) every row of
  `plan.md ## Observability > Preserved observability contract` — Ingest agent + Hub
  ingest pipeline → `IngestObservability{Log,Metrics,Trace}Tests` + ingest
  lifecycle/submission tests (T034/T047); Query agent + Hub query pipeline →
  `QueryLifecycle{LogEvent,Metrics,Trace}Tests` + query agent-side tests (T047); Hub
  request tracing + eval-runner telemetry → `HubRequestTracingTests`,
  `EvalRunnerObservabilityTests`, `EvalObservabilityTests` (T047) — against its
  passing (renamed) test; the Business-Metrics/Log-Events/Trace-Spans tables have
  **zero new rows**, so per the plan's derivation rule no new contract tasks exist to
  audit; (b) every success criterion: SC-001 → T003/T005/T018/T021/T023/T026,
  SC-002 → T001/T002/T027–T043, SC-003 → T035/T044–T046, SC-004 → T023–T025/T048;
  **SC-005 is measured when feature 013 lands** — record it explicitly as a deferred
  gate for feature 013's plan, which must cite the `AgentProfile`/`AgentHost` seam and
  rules D1/D2 (plan Test Strategy). File any gap found as a new task in this file
  before declaring the DoD met.
- [ ] T050 CI enforcement (Constitution Principle IV): verify `.github/workflows/ci.yml`
  runs `backend/tests/Grimoire.ArchTests` (now containing N1/D1/D2 and the
  renamed/re-probed rules) and all renamed test classes in the standard PR pipeline
  with the zero-skip eval gate intact; verify no baseline/ratchet machinery from
  Phase 0 survives anywhere in `backend/tests/Grimoire.ArchTests/` (D1/D2 emptied in
  T023-adjacent tasks, N1's removed in T042) — the rules now enforce outright.
- [ ] T051 [P] Stale-reference sweep: update code comments and non-spec documentation
  that name renamed artifacts as *current* code (e.g. comments referencing
  `ReplayEvalTests`, `ScenarioDefinitions`, `Grimoire.Hub.Submission`), excluding
  historical spec/ADR records (specs 002–009 and accepted ADRs describe their own
  point in time; ADR-013 and the convention document carry the mapping). Check
  `.specify/` agent-context files via the managed update flow if they reference moved
  paths.
- [ ] T052 Run `specs/010-unified-agent-platform/quickstart.md` validation end-to-end
  (steps 1–4: full suite, structural rules incl. manual probe repeatability, optional
  replay-driven agent runs, convention-document spot checks — `ls
  backend/tests/Grimoire.AgentEvals/` shows `IngestReplayEvalTests.cs` next to
  `QueryReplayEvalTests.cs`; `grep -r "class ReplayEvalTests" backend/` is empty) and
  confirm every Expected outcome.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0**: No dependencies — first, non-negotiable (constitution Principle III).
- **Phase 1 (Setup)**: After Phase 0.
- **Phase 2 (Foundational)**: After Phase 1 — BLOCKS all user stories.
- **Phase 3 (US1)**: After Phase 2. Empties the D1/D2 baselines.
- **Phase 4 (US2)**: After Phase 3 for the arch-rule anchor updates (T033/T040 depend
  on US1's host restructuring being final); the convention document (T027) and
  classification tasks (T028–T030) can start any time after T008.
- **Phase 5 (US3)**: After Phases 3 and 4 — it verifies their combined result.
- **Phase 6 (Polish)**: After Phase 5.

### Key Task Dependencies

- T015 depends on T009; T017 depends on T015+T016; T018 depends on T017; T020 depends
  on T015 and learns from T017; T021 depends on T020; T023 depends on T018+T021
  (baselines empty); T024 depends on T020.
- T032 blocks T035 and feeds T040 (`EvalRunnerReplayBoundaryTests` anchors);
  T036–T039 block T040; T028–T041 collectively block T042; T042 blocks T043.
- T044–T048 depend on all of Phases 3–4; T049–T052 depend on Phase 5.

### Parallel Opportunities

- Phase 2: T009–T014 are all [P] (six new independent files); T015 follows T009.
- Phase 3: T025 can run parallel to T023/T024 once T020 lands.
- Phase 4: T028/T029/T030 [P] (independent classifications); T031/T034 [P]
  (different test projects); T037/T038/T039 [P] (disjoint folders).
- Phase 6: T051 [P] against T050.

---

## Parallel Example: Phase 2 (Foundational)

```bash
# All platform components in parallel (independent new files):
Task: "Implement AgentProfile record in backend/src/Grimoire.AgentRuntime/Host/AgentProfile.cs"
Task: "Implement AgentTelemetryBootstrap in backend/src/Grimoire.AgentRuntime/Telemetry/AgentTelemetryBootstrap.cs"
Task: "Implement AgentTracing in backend/src/Grimoire.AgentRuntime/Telemetry/AgentTracing.cs"
Task: "Implement ModelClientFactory in backend/src/Grimoire.AgentRuntime/Composition/ModelClientFactory.cs"
Task: "Implement ErrorSanitizer in backend/src/Grimoire.AgentRuntime/Composition/ErrorSanitizer.cs"
Task: "Implement AgentArgumentReader in backend/src/Grimoire.AgentRuntime/Composition/AgentArgumentReader.cs"
# Then, sequentially:
Task: "Implement AgentHost template in backend/src/Grimoire.AgentRuntime/Host/AgentHost.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 0 (rules + probes, baselines seeded) → Phase 1 (baseline recorded) →
   Phase 2 (platform built, dormant).
2. Phase 3: switch Ingest, verify; switch Query, verify; empty D1/D2 baselines,
   re-probe. **Stop and validate**: the consolidation alone is a mergeable, invisible
   improvement — every suite green, one platform.
3. Phases 4–5 then land the naming convention and the preservation proof; Phase 6
   closes the DoD.

### Incremental Delivery

Each host switch (T017–T019, T020–T022) is independently verifiable against the full
suite — commit after each so a regression bisects to one host's migration. Renames in
Phase 4 land as move-only/rename-only commits (ADR-013 consequence: preserves
reviewability and the no-weakening audit's signal).

### Sequencing Note

This feature must merge before features 011–013 (spec Assumptions); the convention
document's rename map (T027) is what lets those parallel branches rebase mechanically —
finalize it in the same PR, never after.

---

## Notes

- [P] tasks = different files, no dependencies
- Every rename/move uses `git mv` in a rename-only commit (blame continuity, ADR-013)
- The legacy-baseline ratchet exists only inside this feature branch's history; it is
  fully dismantled by T042/T050 — the merged state enforces outright
- Zero new observability rows ⇒ zero new logging/trace-contract tasks (plan.md
  derivation rule); T047 + T049 are the mandated preservation/audit counterparts
- SC-005 is deliberately not implementable here: it is recorded in T049 as feature
  013's entry gate
- Any replay `stale`/`mismatch` failure at any point is a defect in this change set —
  never re-capture recordings inside this feature (research.md R7)
