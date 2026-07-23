---
status: accepted
---

# ADR-011: Shared Agent Runtime, Streaming, and Query Concurrency Model

## Context and Problem Statement

Feature 008 (query-agent) introduces the Query agent: a read-only agent that answers
user questions from wiki content, streams its answer to the UI as it is produced, can
be interrupted mid-answer, and runs concurrently with Ingest and with other Query runs
(clarification session 2026-07-23: fully decoupled from the ADR-008 single-agent slot,
default concurrency limit 3). ADR-002/004/006/007/008 fix the Ingest agent's execution
model (standalone child process, credential scoping, in-process manual tool-use loop
with a guarded file-tool boundary, single system-prompt + default-user-prompt
instruction surface, NDJSON event channel with single-slot FIFO queueing) and
explicitly flag that Query "may or may not need the same treatment — that is a
decision for whichever ADR introduces them, not assumed here" (ADR-002) and that Query
"inherits this pattern (loop + guarded tools + per-agent policy file) when they arrive;
their ADRs need only declare their tool registries and policy scopes" (ADR-006). The
project owner has directed that Ingest and Query share the same agent loop,
distinguished chiefly by system prompt, with tools and policy free to differ per agent.
Query also needs two mechanics no existing ADR covers: token-level answer streaming
over the event channel, and Hub-side bounded concurrency (not a single slot with a
FIFO queue) with user-triggered interruption as a second termination path alongside
liveness failure. These are cross-cutting shapes the still-undecided Lint agent will
also inherit, so they are fixed here rather than assumed inside feature 008.

## Decision Drivers

- Reuse the ADR-006 loop, guarded-tool-executor, and instruction-loading pattern
  instead of duplicating it — the project owner requires one shared agent loop across
  Ingest and Query, differentiated by system prompt, with tools/policy allowed to vary
  per agent (2026-07-23 direction).
- FR-011: the Query agent must have **no wiki-write capability at all** — stronger than
  "defaults to denied by policy"; the absence must be structural (no write tool
  registered in the process), not merely configured.
- SC-003: first answer content visible within 2s (p95) of production, and subsequent
  content within 2s (p95) — requires token-level streaming, not just the existing
  discrete lifecycle/activity events.
- FR-017: Query activity must never block, nor be blocked by, Ingest activity;
  concurrency is bounded (configurable, default 3) and submissions beyond the limit are
  rejected immediately, never queued — the opposite dispatch shape from ADR-008's
  single-slot FIFO queue.
- FR-006: user-triggered interruption must promptly stop the underlying agent run,
  distinct in outcome (`interrupted`) from a liveness-detected crash/hang (`failed`).
- Principle I (hexagonal): any new external-system dependency needs a named port,
  consumer-owned, with adapter containment; Principle II: hermetic harness tests must
  replace the model client and process launcher with fakes, no live LLM calls.
- No new infrastructure or language runtime (Principle IV); ADR-002's spawn/CLI
  contract and ADR-004's credential-scoping-at-spawn must remain unbroken.

## Considered Options

1. **Shared `Grimoire.AgentRuntime` library** (loop, model-client port + Anthropic
   adapter, guarded tool executor, instruction loaders, NDJSON event emitter) referenced
   by both `Grimoire.IngestAgent` and a new standalone `Grimoire.QueryAgent` process;
   each process supplies its own tool registry, policy file, and system prompt. Hub gains
   a sibling `QueryRunCoordinator` (bounded concurrency, reject-over-limit) alongside the
   existing `IngestRunCoordinator` (single-slot FIFO), both built on the existing
   `IAgentProcessLauncher` port.
2. Duplicate the loop/model-client/tool-executor/event-emitter code into a new
   `Grimoire.QueryAgent` project with no shared library.
3. Run Query in-process inside the Hub, referencing `Grimoire.IngestAgent`'s compiled
   `AgentCore` as a library, skipping process-per-turn spawning.
4. Extend `Grimoire.IngestAgent` itself with a `--mode query` flag that swaps tool
   registry/policy/system-prompt path at runtime, instead of a second process/project.

## Decision Outcome

Chosen option: **Option 1.**

### Shared runtime library

- New class library `Grimoire.AgentRuntime`, referenced by both `Grimoire.IngestAgent`
  and the new `Grimoire.QueryAgent`:
  - `Grimoire.AgentRuntime.Core` — `AgentLoop`, `IModelClient`, `ConversationMessage`/
    `ModelTurn`/`ToolDefinition` types (moved verbatim from
    `Grimoire.IngestAgent.AgentCore`).
  - `Grimoire.AgentRuntime.Core.Adapters.Anthropic` — `AnthropicModelClient` (moved from
    `Grimoire.IngestAgent.AgentCore.Adapters.Anthropic`). This becomes the `IModelClient`
    port's adapter namespace, superseding the ADR-010 table entry.
  - `Grimoire.AgentRuntime.Guardrails` — `GuardedToolExecutor` (generalized to accept an
    injected tool registry so agents without a write tool never compile a write branch
    into their process), `WriteJournal` (a no-op stub is valid for agents with no write
    tool), `DeniedActionRecord`.
  - `Grimoire.AgentRuntime.RunEvents` — `RunEventEmitter`, extended with a new
    `answer_chunk` NDJSON event (`{"type":"answer_chunk","taskId":...,"timestamp":...,
    "text":"<delta>"}`) alongside the existing `started`/`heartbeat`/`activity`/
    `completed`/`failed` types (ADR-008 unchanged for those five).
  - `Grimoire.AgentRuntime.Instructions` — `SystemPromptLoader`, `PolicyLoader` (moved
    from `Grimoire.IngestAgent.AgentCore`).
- `IModelClient` gains a streaming turn path: `NextTurnAsync` accepts an optional
  `Action<string>? onTextDelta` callback; `AnthropicModelClient` uses the Anthropic
  streaming Messages API when a callback is supplied, invoking it as text deltas arrive,
  and still returns the same aggregated `ModelTurn` on completion. `AgentLoop` forwards
  the callback, when given, to `RunEventEmitter.EmitAnswerChunk`. Ingest supplies no
  callback (its own assistant text is discarded except the final narrative; behavior is
  byte-for-byte unchanged). Query always supplies one.
- `Grimoire.IngestAgent` keeps its existing tool registry (`list_files`, `read_file`,
  `write_file`), `agents/ingest/policy.json`, `agents/ingest/system-prompt.md`,
  `agents/ingest/default-user-prompt.md`, and its own task-artifact writing — unchanged
  behavior, moved onto the shared library.
- New `Grimoire.QueryAgent` (standalone console app, spawned per Query Turn, same
  ADR-002 child-process/CLI-argument/credential-scoping pattern as Ingest): its own tool
  registry with **exactly** `list_files` and `read_file` — `write_file` is not compiled
  into the process at all, so FR-011's "no wiki-write capability at all" holds
  structurally, not just by policy; `agents/query/policy.json` (read-only scope: `pages/`,
  `index.md`, `log.md`; no write section); `agents/query/system-prompt.md` (fail-closed
  load, FR-003, SHA-256 recorded on the Query Run Artifact). No default-user-prompt
  document — the user's Query Prompt is always supplied per turn; the harness-owned
  message scaffold (conversation history + current prompt + injection framing) still
  wraps it exactly as ADR-007 wraps ingest's effective prompt, so it stays
  non-agent-editable.

### Hub dispatch, concurrency, and interruption

- New `QueryRunCoordinator` (`Grimoire.Hub.QueryDispatch`, sibling to
  `Grimoire.Hub.AgentDispatch`): a counting semaphore sized by a configurable
  `Grimoire:QueryConcurrencyLimit` (default 3, same single-composition-point convention
  as ADR-009's path options). No FIFO queue — a submission at capacity is rejected
  immediately with a "busy" response (FR-017); this is a materially different dispatch
  shape from `IngestRunCoordinator`'s single-slot FIFO and the two coordinators do not
  share state, satisfying "queries never occupy or wait for the ingest slot"
  (clarification 2026-07-23).
- Reuses the existing `IAgentProcessLauncher`/`IAgentProcessHandle` port — no new port —
  with a new `QueryAgentRequest` shape flowing through `StartAsync`. `IAgentProcessHandle
  .Terminate()` is reused for user-triggered interruption as well as liveness cleanup;
  `QueryRunCoordinator` records the resulting terminal state as `interrupted` when the
  Hub itself initiated the stop, versus `failed` when event silence triggered it — the
  two paths call the same mechanism but are labeled by cause.
- `QueryRunCoordinator` accumulates each run's `answer_chunk` events into an in-memory
  partial-answer buffer per turn, so an interrupt or liveness failure can persist
  whatever text already streamed (FR-006, FR-015) into the Query Run Artifact.

### Persistence and conversation context

- Query Run Artifacts are written **entirely by the Hub** — the Query agent process has
  no write capability at all, so unlike Ingest's agent-owned task artifact, 100% of the
  Query Run Artifact's fields are harness-written. Stored as one file per turn under
  `<base>/data/query-runs/` — internal runtime bookkeeping, not domain content a user
  edits in Obsidian, so per ADR-009 ("internal runtime data lives beneath `<base>/data`")
  and ADR-003 (domain vs. operational split) it stays outside `wiki/` and outside git.
  This is a direct application of both ADRs' existing pattern, not a new persistence
  boundary.
- No server-side conversation store. Each turn submission carries the conversation's
  prior turns (prompt + answer, including partial text of interrupted turns) supplied by
  the browser, which already holds them as the on-screen conversation (spec Assumptions:
  conversations are session-scoped UI state). The Hub's message scaffold formats the
  supplied history into the new process's initial conversation the same way ADR-007's
  scaffold wraps ingest's source. Traceability for audit purposes is per-turn via the
  Query Run Artifact, not via a reconstructed server-side conversation.

### Realtime delivery

- New sibling `QueryLifecycleHub` / `QueryLifecyclePublisher` (`Grimoire.Hub.Realtime`,
  route `/hubs/query-lifecycle`), broadcasting `queryAnswerChunk` (turnId, text delta)
  and `queryTurnChanged` (turnId, fromState, toState, reason) — kept structurally
  separate from `IngestLifecycleHub` so query realtime traffic can never be coupled to
  ingest's (FR-017), at the cost of a second SignalR connection from the browser
  (mirrors the existing `ConnectionStatusIndicator` pattern per connection).

### Hexagonal ports and containment (amends ADR-010)

| Port | Owner namespace | Production adapter → adapter namespace | Test fake |
| --- | --- | --- | --- |
| `IModelClient` | `Grimoire.AgentRuntime.Core` (moved from `Grimoire.IngestAgent.AgentCore`) | `AnthropicModelClient` → `Grimoire.AgentRuntime.Core.Adapters.Anthropic` | `FakeModelClient` |
| `IAgentProcessLauncher` | `Grimoire.Hub.AgentDispatch` (unchanged, now shared by `IngestRunCoordinator` and `QueryRunCoordinator`) | `AgentProcessHost` → `Grimoire.Hub.AgentDispatch.Adapters.AgentProcess` | `FakeAgentProcess` |

New/updated containment rules in `Grimoire.ArchTests` (each with a Red/Green probe):

- C6 (supersedes C2): `Anthropic` SDK only in
  `Grimoire.AgentRuntime.Core.Adapters.Anthropic`.
- C7: filesystem-write APIs reachable only from `Grimoire.AgentRuntime.Guardrails` and
  each agent process's own task/run-artifact writer namespace — never from
  `Grimoire.QueryAgent`'s tool-dispatch path, since it never registers a write tool.
  Extends the existing guarded-write boundary rule to cover both agent process
  assemblies.

### Consequences

- Good, because Ingest and Query share one tested loop, model-client seam, and guarded
  tool executor — exactly the reuse the project owner directed — while each keeps its
  own tool registry, policy file, and system prompt, so their authority never leaks into
  each other.
- Good, because Query's "no write capability at all" (FR-011) is enforced by what the
  process compiles and registers, not only by a policy file a future edit could loosen.
- Good, because bounded-concurrency dispatch and single-slot FIFO dispatch coexist
  without shared state, matching the clarified requirement that Query and Ingest never
  wait on each other.
- Good, because streaming reuses the existing NDJSON stdout channel (one new event type)
  rather than introducing a second transport, keeping ADR-008's hermetic-testability and
  no-new-infrastructure properties intact.
- Bad, because moving Ingest's existing `AgentCore` files into `Grimoire.AgentRuntime` is
  refactor churn that breaks `git blame` continuity; mitigated by a move-only commit, the
  same tradeoff already accepted in ADR-010.
- Bad, because the browser now holds two SignalR connections (ingest, query) instead of
  one; accepted so FR-017's independence is structurally visible rather than coupling
  both features' realtime code paths through a shared hub.
- Neutral, because the still-undecided Lint agent inherits the same shared-runtime
  pattern (own tool registry/policy/system-prompt on top of `Grimoire.AgentRuntime`)
  when it arrives.

## More Information

Detailed rationale: `specs/008-query-agent/research.md`. Contracts:
`specs/008-query-agent/contracts/` (query-run-events.md, guarded-read-only-tools.md,
query-conversation-api.md). This ADR must be **Accepted** before `/speckit-tasks` runs
for feature 008 (Constitution, Spec-Kit Workflow step 4) — accepted here per the
project's established convention of author sign-off during `/speckit-plan` (consistent
with ADR-002 through ADR-010, all accepted the same way).
