# Implementation Plan: Conversation Records Replace Query-Run Artifacts

**Branch**: `011-query-conversations` | **Date**: 2026-07-27 | **Spec**: `specs/011-query-conversations/spec.md`

**Input**: Feature specification from `/specs/011-query-conversations/spec.md`

## Summary

Replace the one-file-per-turn Query Run Artifacts
(`<base>/data/query-runs/<conversationId>/<turnId>.md`) with one durable
**Conversation Record** per conversation at
`<base>/data/conversations/<conversationId>.md`: the complete transcript (every
turn's prompt + full answer in order, readable as a dialogue) plus per-turn
bookkeeping (state, reasons, timestamps, instruction identity/hash, denied
actions, model/loop usage), appended as each turn reaches a terminal state and
never rewritten. The record additionally becomes the **source of follow-up
context**: the browser submits only the prompt, and the Hub builds the agent's
prior-turn scaffold from the record — making FR-006's context/record consistency
hold by construction (single source). User-facing behavior from feature 008
(streaming, interruption, follow-ups, one active turn, concurrency limit 3) is
unchanged; only the persistence shape and the submission payload change. Full
rationale: `research.md`; superseding decision: `docs/adr/ADR-014-query-conversation-records.md`
(**proposed** — supersedes ADR-011's "Persistence and conversation context"
section only).

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript / Svelte 5 (frontend) —
unchanged existing stack (ADR-001).

**Primary Dependencies**: ASP.NET Core + SignalR (existing). No new packages, no
new external system, no new port (persistence exemption, Constitution I).

**Storage**: One append-only markdown file per conversation under
`<base>/data/conversations/` (format contract:
`contracts/conversation-record-format.md`), resolved via
`GrimoirePathOptions.ConversationsDir` (default dir name `conversations`,
ADR-009 single composition point). `data/query-runs/` is retired (no migration,
FR-008). No database change.

**Testing**: xUnit (`Grimoire.IntegrationTests`) with the existing
`FakeAgentProcess`/`FakeModelClient` doubles — every success criterion in this
feature is a deterministic harness guarantee, hermetically testable, no live LLM
calls; `Grimoire.ArchTests` for the retired-location tripwire (Red/Green probe);
Vitest for the small frontend payload change. Feature 008's agent-behavior evals
(ADR-012 replay) must keep passing unchanged — the agent process contract is
deliberately untouched (research.md R7).

**Target Platform**: Same as existing Hub — cross-platform .NET processes, local
dev and CI; SvelteKit frontend.

**Project Type**: Web application (existing `backend/` + `frontend/` split).

**Performance Goals**: None new — recording is off the streaming hot path (append
on terminal transition only); context load is cached in memory, file parse only
after a Hub restart.

**Constraints**: FR-003 append-only/never-rewrite; FR-006 fail-closed context
consistency; FR-009 zero user-facing behavior change (streaming, interruption,
409 one-active-turn, 503 concurrency limit 3).

**Scale/Scope**: Single-user context; at most `QueryConcurrencyLimit` (3)
concurrent conversations appending, each to its own file, appends serialized per
conversation by the one-active-turn guard.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Principle I (Domain Architecture, Strategic DDD & Hexagonal Boundaries)**:
  PASS. No new external system and no new port: the Conversation Record store is
  local-filesystem persistence, explicitly port-exempt ("Persistence exemption")
  — `ConversationRecordStore` is a concrete class injected directly, confined to
  its namespace (`Grimoire.Hub.QueryConversations`). Existing containment rules
  (ADR-010/011/012) are untouched and keep passing; a new tripwire rule guards
  the retired `query-runs` location.
- **Principle II (Pragmatic Testing Strategy)**: PASS. All five success criteria
  are deterministic harness guarantees (100%), hermetically tested with
  `FakeAgentProcess`/`FakeModelClient` against the real filesystem — no mocked
  persistence, no live LLM calls. The spec's success-criteria split is clean: no
  agent-judgment criteria exist here (the spec says so explicitly), and no agent
  judgment is being reimplemented deterministically — recording and context
  transport are harness mechanics. Feature 008's evaluation thresholds remain in
  force unchanged.
- **Principle III (ADR-Driven & Test-Enforced Architecture)**: **CONDITIONAL
  PASS.** All ADRs in `docs/adr/` were read; the table below lists the
  constraints. This plan supersedes part of an Accepted decision (ADR-011), so a
  new ADR is mandatory: `ADR-014-query-conversation-records.md` is drafted with
  status **proposed** — the project owner will sign off. **Gate: ADR-014 MUST
  reach Accepted before `/speckit-tasks` is invoked** (constitution workflow
  step 4). The first task in `tasks.md` must be the structural tripwire test
  (no production IL literal `query-runs`) with its Red/Green probe.
- **Principle IV (Behavioral & Observable Engineering)**: PASS. No new
  infrastructure (markdown files under the existing data dir; OTel backend per
  ADR-005 unchanged). Full `## Observability` section below, including the
  explicit retire/keep/add delta against feature 008's signals, with the
  mandatory logging-contract and trace-contract derivation rules for `tasks.md`.
- **Principle V (Agentic Core & Deterministic Harness)**: PASS. Harness-only
  feature: the record's composition (transcript layout, bookkeeping, escaping)
  is persistence mechanics, not wiki-content judgment; no instruction file
  changes; the guarded tool boundary and the agent's harness-owned message
  scaffold are untouched (the scaffold's *data source* changes from
  client-supplied to record-sourced — its shape, and everything the agent sees,
  is identical). No wiki writes are introduced anywhere; the Query agent still
  has no write capability at all.

Re-check after Phase 1 design: no change — no violations, no Complexity Tracking
needed. The only open gate is ADR-014 acceptance (Principle III above).

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*
(All of ADR-001 through ADR-012 were read; ADR-013 is being drafted by the
sibling feature-010 plan — agent-platform packaging — and is context only, not a
constraint on this feature.)

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend/frontend tech stack | Record store and API revision stay on the existing .NET/SvelteKit stack; no new language, runtime, or package. |
| ADR-002 | Ingest agent execution model | The Query agent child-process/CLI/stdin contract is not altered: `QueryConversationInput` (prompt + prior turns) keeps its shape; only the Hub-side origin of the prior turns changes. |
| ADR-003 | Domain vs. operational state persistence | Conversation Records are operational harness bookkeeping: under `<base>/data/`, outside `wiki/`, outside git — same split the retired artifacts followed. |
| ADR-004 | Credential scoping | Untouched; no credential path changes. Constrains only that the record store never needs or sees the API key. |
| ADR-005 | Observability backend | New `query.conversation.*` signals export via the existing OTel SDK/Aspire setup; CI verification via in-memory exporter assertions, no new collector. |
| ADR-006 | Agent tool-use loop & guarded tool boundary | Denied-action records flowing from the guarded executor into the terminal event metadata are preserved verbatim into the record's per-turn `denied_actions` (SC-002); the tool boundary itself is untouched. |
| ADR-007 | Agent instruction surface | Instruction identity (system-prompt path + SHA-256) and policy identity keep being recorded per turn — relocated from artifact frontmatter to the turn's bookkeeping block. The harness-owned, non-agent-editable message scaffold is unchanged. |
| ADR-008 | Agent event channel & run supervision | Terminal states, liveness supervision, and the `answer_chunk` accumulation that produce a turn's recorded outcome/partial answer are reused as-is; the record is appended at exactly the existing terminal-transition point. |
| ADR-009 | Runtime path configuration | The record location is added via the single composition point: `GrimoirePathOptions.ConversationsDir` (+ `DefaultConversationsDirName = "conversations"`), resolved/auto-created by `GrimoirePathResolver`, reported as `conversations_dir`; `QueryRunsDir`/`DefaultQueryRunsDirName` are deleted. No ambient discovery. |
| ADR-010 | Hexagonal ports & adapter namespaces | Persistence exemption applies (no port for the record store); adapter containment: filesystem writing confined to `Grimoire.Hub.QueryConversations` alongside the existing Hub writer namespaces; existing C-rules keep passing. |
| ADR-011 | Query agent shared runtime & concurrency model | Binding except its "Persistence and conversation context" section, which ADR-014 supersedes. Streaming, bounded concurrency (limit 3, reject-over-limit), interruption semantics, realtime delivery, and the shared-runtime/port table remain fully in force and unchanged. |
| ADR-012 | Eval runner & recorded replay | Replay fingerprints must not drift: the agent's stdin contract and model-port request stream stay byte-identical for a given conversation (research.md R7), so existing recordings remain valid. |
| ADR-014 | Query conversation records (new, this plan, **proposed**) | Fixes the record-per-conversation shape, append-only mechanics, record-as-context-source, the retirement of per-turn artifacts, and the `conversations` path — must be Accepted before `/speckit-tasks`. |

**New ADR required?**: Yes — `docs/adr/ADR-014-query-conversation-records.md`,
drafted as part of this plan with status **proposed** (owner sign-off pending;
Constitution Check gate above is conditional on its acceptance).

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

No agentic surface is added or changed — harness-only feature. For the avoidance
of doubt:

| Capability | Side | Where it lives |
|------------|------|-----------------|
| Conversation Record composition (transcript layout, bookkeeping, escaping, append) | Harness | `Grimoire.Hub.QueryConversations.ConversationRecordStore` |
| Follow-up context assembly (record → scaffold) | Harness | `QueryRunCoordinator` + record store (same non-agent-editable scaffold as 008) |
| Answer content, follow-up resolution, grounding | Agentic core | `agents/query/system-prompt.md` — **unchanged** by this feature |
| Guardrails / denied-action production | Harness | `Grimoire.AgentRuntime.Guardrails` — **unchanged**; denials are only relocated in storage |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

All success criteria in this spec are deterministic harness guarantees — there
are no agent-judgment criteria (spec: "no agent-judgment success criteria
apply"). Every test below is hermetic: `FakeAgentProcess`/`FakeModelClient`
doubles, real filesystem under a temp base dir, fake clock where timing matters,
no live LLM calls, no API keys.

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001 (exactly one record per conversation; every terminal turn present with prompt, full answer, order, complete bookkeeping) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess` scripted multi-turn runs, temp base dir | 3-turn conversation incl. follow-up; two concurrent conversations | Asserts one file per conversation, blocks in position order, all bookkeeping fields vs. terminal metadata; concurrent conversations never cross-contaminate |
| SC-002 (100% of denied tool actions recorded with reasons) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess` emitting terminal metadata with scripted denials | Denial fixture (`read_file` out-of-scope) | Asserts `denied_actions` in the turn's bookkeeping block matches the denial list incl. reason/targets, and escaping rules hold for hostile target strings |
| SC-003 (terminal turns survive browser loss/Hub restart; in-flight turns recorded with partial answer + supervision-consistent terminal state) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess` (controllable silence/termination), fake `TimeProvider`, coordinator+store re-instantiated over the same base dir (= Hub restart) | Two finished turns + one mid-stream kill; liveness-window fixture | Asserts record on disk after "restart" contains finished turns; killed turn appears `failed`/`interrupted` with accumulated partial answer per existing supervision rules |
| SC-004 (0 new files in retired location; all new activity in records only) | Deterministic guarantee | Hermetic integration test + structural (ArchTests) | `FakeAgentProcess` | N/A | Integration: after full turn lifecycle, `data/query-runs/` absent/empty. Structural tripwire: no production assembly contains IL literal `query-runs` (ADR-009 idiom), proven with Red/Green probe — first task in `tasks.md` |
| SC-005 (prior-turn context delivered to the agent matches the recorded transcript at submission time) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess` capturing the `QueryAgentRequest` handed to the launcher port | Multi-turn conversation incl. interrupted turn with partial answer; Hub-restart variant (context hydrated from file) | Parses the record with the contract parser and asserts tuple-equality with the captured `PriorTurns` — both from-cache and from-file paths |

Supporting deterministic tests (not SC-numbered but contract-bearing, feeding
`tasks.md`): record-format parser round-trip incl. injection fixtures (bodies
containing sentinels/headings), trailing-partial-block recovery, fail-closed
`conversation_record_unreadable` (500) path, `conversationId` validation (400),
stale-client `priorTurns` field ignored, append-failure isolation (turn outcome
and `queryTurnChanged` publish unaffected — regression-fixing the current
unguarded `_artifactWriter.WriteAsync` call), and the full logging/trace
contract tests listed under Observability. Frontend: Vitest asserts the
submission payload contains only `prompt` and the UX flows of 008 are unchanged.

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

Delta against feature 008 (`specs/008-query-agent/plan.md ## Observability`):

- **Retired**: span `query_agent.finalize_artifact` (agent-side,
  `Grimoire.QueryAgent/Program.cs`) — removed with the artifact mechanism, not
  renamed (it was vestigial: the Query agent never wrote the artifact). No
  feature-008 **log event or metric** is retired or renamed: the 008 artifact
  writer had no dedicated log event, metric, or span of its own (verified in
  code), so the recording path's signals below are strictly additive.
- **Unchanged and still mandatory**: all other 008 rows — log events
  `query.turn.created`, `query.instructions.loaded`,
  `query.instructions.load_failed`, `query.tool.denied`, `query.turn.completed`,
  `query.turn.interrupted`, `query.turn.failed`, `query.submission.rejected`,
  `query.lifecycle.published`; metrics `query.turns_total`,
  `query.concurrent_runs`, `query.answer_chunks_total`, `query.tool_calls_total`,
  `query.turn_duration_seconds`, `query.submissions_rejected_total`; spans
  `hub.query.submit`, `hub.query.spawn_agent`, `hub.query.run_supervision`,
  `hub.query.handle_run_event`, `hub.query_lifecycle.publish_update`,
  `query_agent.run`, `query_agent.load_instructions`, `query_agent.model_turn`,
  `query_agent.tool_call`. Their existing tests must keep passing.
- **Added**: the `query.conversation.*` rows below.

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|--------------|--------|
| `query.conversation.turns_recorded_total` | Counter | Turns appended to a Conversation Record | `outcome=completed\|interrupted\|failed` |
| `query.conversation.record_append_failures_total` | Counter | Failed record appends (turn outcome unaffected) | none |
| `query.conversation.context_loads_total` | Counter | Prior-turn context loads at submission | `source=memory\|record\|empty` |
| `query.conversation.record_load_failures_total` | Counter | Fail-closed context loads (unreadable record) | none |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-------------------|
| `query.conversation.record_created` | INFO | First terminal turn creates the record file | `conversation_id`, `path` |
| `query.conversation.turn_recorded` | INFO | A terminal turn's block is appended | `conversation_id`, `turn_id`, `position`, `outcome` |
| `query.conversation.record_append_failed` | ERROR | Appending a turn block fails | `conversation_id`, `turn_id`, `reason` |
| `query.conversation.context_loaded` | INFO | Prior-turn context assembled for a submission | `conversation_id`, `turn_count`, `source` |
| `query.conversation.record_load_failed` | ERROR | Record exists but is unreadable — submission rejected fail-closed | `conversation_id`, `reason` |

**Derivation rule (MANDATORY)**: every row above maps to concrete `tasks.md`
work covering all three categories — implementation with stable event name and
mandatory fields (extending `QueryLifecycleLogEvents`' idiom in a
`Grimoire.Hub.QueryConversations` log-events type), deterministic integration
tests validating event name/level/mandatory fields, and CI enforcement in the
standard PR pipeline — per the constitution's logging-contract requirement.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|------------|
| `hub.query.load_conversation_context` | `hub.query.submit` | `conversation_id`, `turn_count`, `source` |
| `hub.query.record_turn` | `hub.query.run_supervision` (supervision-detected terminals) or the interrupt HTTP request root (user interruption) | `conversation_id`, `turn_id`, `outcome` |

**Derivation rule (MANDATORY)**: every row above maps to concrete `tasks.md`
work covering implementation (span creation with declared parent/child +
attributes), deterministic integration tests (name/linkage/correlation via
shared `turn_id`/`conversation_id`, in-memory exporter per ADR-005), and CI
enforcement, per the constitution's trace-contract requirement. Logs/metrics of
the recording path are emitted within these span contexts and correlate via
`turn_id`/`conversation_id`.

## Project Structure

### Documentation (this feature)

```text
specs/011-query-conversations/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── conversation-record-format.md
│   └── query-conversation-api.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/src/Grimoire.Hub/
├── QueryConversations/                  # NEW namespace (Grimoire.Hub.QueryConversations)
│   ├── ConversationRecordStore.cs       # append terminal turns, load/cached context, per-conversation lock
│   ├── ConversationRecordFormat.cs      # grimoire-conversation/1 writer+parser (length-delimited bodies, escaping)
│   ├── RecordedTurn.cs                  # parsed turn (bookkeeping + prompt/answer)
│   └── ConversationRecordLogEvents.cs   # query.conversation.* structured log events
├── QueryDispatch/
│   └── QueryRunCoordinator.cs           # CHANGED: context from record store; append (guarded) replaces artifact write; new spans
├── QueryRunArtifact/                    # DELETED (QueryRunArtifactWriter retired, FR-007)
├── QuerySubmission/
│   ├── QuerySubmissionEndpoints.cs      # CHANGED: priorTurns removed, conversationId validation, 500 unreadable path, Hub-assigned position
│   └── QuerySubmissionValidator.cs      # + conversationId rule (prompt rules unchanged)
├── Runtime/Paths/
│   ├── GrimoirePathOptions.cs           # CHANGED: ConversationsDir replaces QueryRunsDir
│   ├── GrimoirePathResolver.cs          # CHANGED: conversations_dir resolution/auto-create/report
│   └── ResolvedGrimoirePaths.cs         # CHANGED: ConversationRecordPathFor replaces QueryRunArtifactPathFor
└── HubMetrics.cs                        # + query.conversation.* instruments

backend/src/Grimoire.QueryAgent/
└── Program.cs                           # CHANGED: query_agent.finalize_artifact span removed (stdin/scaffold contract untouched)

backend/tests/
├── Grimoire.ArchTests/                  # + retired-location tripwire (no IL literal "query-runs") with Red/Green probe
└── Grimoire.IntegrationTests/           # + record store/format/consistency/restart/logging/trace-contract tests; artifact-writer tests removed

frontend/src/lib/
├── components/QueryConversation.svelte  # CHANGED: submission payload = prompt only (UI/UX unchanged, FR-009)
└── services/ (query submission client)  # CHANGED: drop priorTurns from request body

data/conversations/                      # NEW runtime location (ADR-009), git-ignored
data/query-runs/                         # RETIRED — no new files (SC-004); existing files disposable
```

**Structure Decision**: Existing `backend/` + `frontend/` split, unchanged. One
new Hub namespace (`QueryConversations`) following the established
namespace-per-concern convention; one namespace deleted (`QueryRunArtifact`).
No new assembly, no new process, no new port (Constitution I's
no-extra-assemblies default).

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified

No violations — not applicable.
