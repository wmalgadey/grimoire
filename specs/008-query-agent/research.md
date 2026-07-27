# Research: Interactive Wiki Query Process

Companion to `plan.md`. Each item resolves one Technical Context unknown or one
integration/best-practice question raised by the spec. Full architectural rationale for
the cross-cutting decisions lives in ADR-011; this document focuses on the reasoning
that fills in `plan.md`'s Technical Context and the choices ADR-011 doesn't need to
carry (frontend surface, evaluation harness reuse, config keys).

## R1: Shared agent loop vs. per-agent duplication

- **Decision**: Extract `Grimoire.AgentRuntime` (loop, `IModelClient` + Anthropic
  adapter, guarded tool executor, instruction loaders, NDJSON event emitter), referenced
  by both `Grimoire.IngestAgent` and new `Grimoire.QueryAgent`. Full rationale, options,
  and containment rules: ADR-011.
- **Rationale**: explicit project-owner direction (2026-07-23): one agent loop, per-agent
  system prompt, tools/policy allowed to vary. Matches ADR-006's own anticipation
  ("Query and Lint agents inherit this pattern... their ADRs need only declare their
  tool registries and policy scopes").
- **Alternatives considered**: duplicate the loop into a second project (rejected: two
  copies of the same ~500 lines drift and double the surface `Grimoire.ArchTests` must
  cover); run Query in-process in the Hub (rejected: breaks ADR-002 process isolation,
  a crashing/hanging model call would take the Hub down with it); a `--mode` flag on the
  existing Ingest binary (rejected: FR-011's "no write capability at all" is much
  stronger as "the binary never links a write tool" than "a flag defaults to off").

## R2: Token-level answer streaming transport

- **Decision**: extend the existing ADR-008 NDJSON stdout event channel with one new
  event type, `answer_chunk` (delta text), rather than introducing a second transport
  (HTTP SSE from the agent, a socket, etc.). `IModelClient.NextTurnAsync` gains an
  optional `onTextDelta` callback; `AnthropicModelClient` switches to the Anthropic
  streaming Messages API when a callback is supplied.
- **Rationale**: SC-003 requires first-content-within-2s and subsequent-content-within-2s
  (p95). Waiting for a whole non-streamed model turn before emitting anything would make
  a long answer's first visible content arrive only at the end — defeating the point of
  streaming. Reusing stdout keeps ADR-008's hermetic-testability and no-new-infra
  properties: the Hub already reads stdout line-by-line; scripting `answer_chunk` lines
  in a `FakeAgentProcess` test double requires no new test infrastructure.
- **Alternatives considered**: a second channel (e.g., the Query agent opens its own
  loopback HTTP connection back to the Hub) — rejected as a needless new network surface
  for a parent↔child relationship (the same reasoning ADR-008 used to reject Options 2/3
  for the existing event channel); polling the agent's partial output file — rejected,
  adds filesystem polling latency and a second race-prone state surface.

## R3: Read-only tool registry and structural enforcement (FR-011, FR-014)

- **Decision**: `Grimoire.QueryAgent` registers exactly `list_files` and `read_file`;
  `write_file` is not compiled into the process. The shared `GuardedToolExecutor` from
  `Grimoire.AgentRuntime.Guardrails` still evaluates every call against
  `agents/query/policy.json` (read-only scope: `pages/`, `index.md`, `log.md`) as a
  second layer, so an attempt to read outside the wiki (e.g. `../data/query-runs/`) is
  denied and recorded exactly like Ingest's out-of-scope denials (FR-012, FR-013).
- **Rationale**: FR-011 says the Query agent must have no write capability "at all" —
  stronger than "policy denies writes by default." Not registering the tool means the
  model is never even offered `write_file` as a choice, and there is no code path in the
  Query process that could execute a write regardless of what a policy file said. FR-014
  (structural test with Red/Green probe) extends the existing
  `GuardedWriteBoundaryRuleTests` idiom (Mono.Cecil IL scan) to `Grimoire.QueryAgent`,
  asserting zero filesystem-write API calls reachable outside
  `Grimoire.AgentRuntime.Guardrails`.
- **Alternatives considered**: register `write_file` in Query but rely solely on
  `policy.json` denying it — rejected: satisfies FR-012/FR-013 but not FR-011's "at all,"
  and leaves one policy-file edit away from a live vulnerability.

## R4: Bounded concurrency dispatch vs. FIFO queue reuse

- **Decision**: new `QueryRunCoordinator`, independent of `IngestRunCoordinator`, using a
  counting semaphore sized by `Grimoire:QueryConcurrencyLimit` (default 3). At capacity,
  `SubmitTurnAsync` returns a "busy" rejection immediately — no queueing. Full rationale
  in ADR-011.
- **Rationale**: clarification 2026-07-23 confirms Query runs are "fully decoupled" from
  the Ingest single-agent slot and are governed only by their own concurrency limit; the
  spec's edge case for "beyond that limit" explicitly says "rejected immediately with a
  clear busy message rather than silently queued" — the opposite of ADR-008's FIFO
  design for Ingest.
- **Alternatives considered**: reuse/extend `IngestRunCoordinator` with a "query mode" —
  rejected, its `_runningTaskId`/FIFO-queue design is single-slot by construction and
  retrofitting bounded concurrency onto it risks regressing Ingest's tested behavior for
  no shared benefit, since the two coordinators share no state per the clarification.

## R5: Interruption mechanism

- **Decision**: `IAgentProcessHandle.Terminate()` (already used for liveness-failure
  cleanup) is reused for user-triggered interruption. `QueryRunCoordinator` labels the
  resulting terminal state `interrupted` when the Hub itself initiated the stop (a
  client call), vs. `failed` when event silence triggered it — same mechanism, different
  recorded cause.
- **Rationale**: FR-006 requires production to halt "promptly (including stopping the
  underlying agent run)"; a forceful process-tree kill is already implemented, tested,
  and satisfies "promptly" trivially (no graceful-shutdown handshake to wait on). Terminal
  states are final (FR-007) and idempotent guards already exist in
  `IngestRunCoordinator.FinishRunAsync`'s pattern — `QueryRunCoordinator` reuses the same
  idiom (lock-guarded, first-terminal-transition-wins).
- **Alternatives considered**: a graceful stop signal (SIGINT to the child, agent checks
  a cancellation flag between streamed tokens) — rejected as unnecessary complexity: the
  spec has no requirement that an interrupted run finish its current tool call cleanly,
  only that the partial answer already delivered stays visible, which the Hub-side
  chunk buffer already guarantees independent of how abruptly the process dies.

## R6: Conversation context transport (no server-side conversation store)

- **Decision**: the browser's on-screen conversation (prompts + answers, including
  partial text of interrupted turns) is sent by the client with every follow-up turn
  submission; the Hub holds no persisted conversation object. Full rationale in ADR-011.
- **Rationale**: spec Assumptions confirm conversations are session-scoped UI state, one
  per browser window, with no in-UI conversation browser and no cross-reload persistence
  requirement — the browser is already the durable-enough source of truth for what FR-009
  needs. Building a server-side conversation store would duplicate that state for no
  requirement it satisfies, and would need its own reload/expiry semantics the spec
  doesn't ask for.
- **Alternatives considered**: Hub-side in-memory conversation cache keyed by
  conversation id — rejected: adds a second copy of state that must stay in sync with
  the browser's, for zero additional capability (a Hub restart would still lose it, same
  as the browser losing it on reload, so no reliability gain).

## R7: Query Run Artifact storage location

- **Decision**: `<base>/data/query-runs/<conversationId>/<turnId>.md`, written entirely
  by the Hub (the agent process has no write capability, R3). Full rationale in ADR-011.
- **Rationale**: applies ADR-009's already-established rule ("internal runtime data
  lives beneath `<base>/data`") and ADR-003's domain/operational split (Query Run
  Artifacts are harness bookkeeping, not user-editable wiki content) — no new
  persistence boundary, just placing a new record type in the location those ADRs
  already designate for it.
- **Alternatives considered**: `wiki/tasks/` alongside Ingest's task artifacts —
  rejected: spec Assumptions explicitly exclude task artifacts from what "the wiki"
  means to the Query agent, and placing Query's own artifacts there would put them
  inside the Query agent's own read-denial scope, which is confusing; also they are not
  domain content a user would want committed/diffed in the wiki's own git history.

## R8: Realtime transport for streamed answers and turn state

- **Decision**: new `QueryLifecycleHub` (`/hubs/query-lifecycle`) + `QueryLifecyclePublisher`,
  sibling to the existing `IngestLifecycleHub`/`IngestLifecyclePublisher`, broadcasting
  `queryAnswerChunk` and `queryTurnChanged`. Frontend opens a second SignalR connection
  with its own `ConnectionStatusIndicator` instance (component already supports multiple
  instances — it is a pure props-in component).
- **Rationale**: reuses the exact mechanism features 003/004 established (SignalR
  broadcast hub, idempotent-by-eventId client application, automatic reconnect +
  connection-state projection) per the spec's Assumptions, while keeping query realtime
  traffic structurally independent of ingest's (FR-017) — a shared hub would couple the
  two features' message shapes and reconnection/backpressure behavior for no benefit.
- **Alternatives considered**: multiplex query events onto `IngestLifecycleHub` — rejected,
  couples unrelated concerns and would force every ingest-board client to also
  deserialize query event shapes it never uses.

## R9: Evaluation tests for agent-judgment success criteria (SC-007..SC-010)

- **Decision**: reuse the `Grimoire.AgentEvals` harness and NIM-endpoint judge pattern
  established in feature 007 (`specs/007-eval-tests-nim-endpoint`) — sampled real-model
  runs against fixture wiki content, scored by an LLM-judge rubric, thresholds asserted
  in CI as a distinct evaluation suite (not unit/integration tests).
- **Rationale**: Constitution Principle II requires agent-judgment outcomes to be
  verified by evaluation-style tests with explicit thresholds, never reimplemented as
  deterministic code; feature 007 already solved "how do we run and judge sampled LLM
  output against a threshold in this project," so Query's SC-007..SC-010 are new fixture
  sets and rubrics on the same harness, not a new evaluation mechanism.
- **Alternatives considered**: hand-written keyword-matching assertions against sample
  answers — rejected outright by Principle II (would reimplement judgment as
  deterministic code, the exact violation the constitution names).

## R10: Frontend query surface placement

- **Decision**: new route `frontend/src/routes/query/+page.svelte` (sibling to the
  existing `board`/`tasks/[taskId]` routes), with new components `QueryConversation
  .svelte` (turn list + streaming answer rendering) and `QueryPromptForm.svelte`
  (input + submit + stop control), plus a `queryLifecycleClient.ts` service mirroring
  `ingestLifecycleClient.ts`'s shape (start/stop/on*/onConnectionStateChanged).
- **Rationale**: features 003/004 already establish the pattern of one SvelteKit route
  per capability with a thin SignalR-client service module and pure, independently
  testable event-application functions (`applyLifecycleEvent`); mirroring it for query
  keeps the two capabilities visually and structurally parallel without introducing a
  new frontend architecture.

## Technical Context resolution summary

| Unknown | Resolution |
|---|---|
| Language/Version | C# / .NET 10 (backend, matches existing `Grimoire.Hub`/`Grimoire.IngestAgent`); TypeScript / Svelte 5 (frontend, matches existing `frontend/`) |
| Primary Dependencies | ASP.NET Core, SignalR, Anthropic Messages API (streaming), `Grimoire.AgentRuntime` (new shared library), `@microsoft/signalr` (frontend, already a dependency) |
| Storage | Markdown files under `<base>/data/query-runs/` (Query Run Artifacts, ADR-009); no new database |
| Testing | xUnit integration tests (`Grimoire.IntegrationTests`) with `FakeAgentProcess`/`FakeModelClient`; `Grimoire.ArchTests` (NetArchTest + Mono.Cecil IL scan) for structural rules; `Grimoire.AgentEvals` for SC-007..SC-010; Vitest + Testing Library for frontend components |
| Target Platform | Same as existing Hub/agents — cross-platform .NET console/web processes, local dev + CI |
| Project Type | Web application (existing backend + frontend split) |
| Performance Goals | SC-003: first/subsequent answer content visible within 2s (p95) |
| Constraints | SC-004: interruption halts delivery within 2s; FR-017: default concurrency limit 3, configurable |
| Scale/Scope | Single-user context (no auth/multi-user), one active conversation per browser window, up to `QueryConcurrencyLimit` concurrent turns Hub-wide |
