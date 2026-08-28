# Assessment: Apache Maka for Grimoire's Agent Harness

> **Role of this document.** Decision context (source material), in the sense of the
> Document Map in `CLAUDE.md`: it is **not binding** for SDD. Its declared reader is
> the author of a future ADR touching the agent process/event-channel boundary (the
> ADR-036/ADR-037/ADR-038/ADR-039 family) or the guarded-tool boundary (ADR-006,
> ADR-034) — e.g., if crash-recovery, turn-resumption, or a richer execution log ever
> becomes a real requirement. Statements here become enforceable only once extracted
> into the constitution or an Accepted ADR.

**Date:** 2026-08-28
**Trigger:** Review of [apache/maka](https://github.com/apache/maka) (surfaced via
console.dev) — question: is this useful for Grimoire, and should it flow into our
architecture?

**Verdict (TL;DR):** Maka is a well-designed local-first agent workspace, and its
harness/log split independently converges on the same shape as Grimoire's own
Principle V. But it solves a different problem (a general-purpose, interactive,
foreign-agent-friendly workspace) on a different stack (Node/TypeScript/Electron vs.
our .NET backend). Nothing here is adoptable as a dependency. Keep it as a reference
architecture for two open questions Grimoire has not yet had to answer at scale: crash
recovery and a structured execution log.

---

## 1. What apache/maka does

[apache/maka](https://github.com/apache/maka) is an Apache Incubator project (Apache
2.0, ~3.9k stars, highly active): a "local-first AI agent workspace" for running coding
agents on the user's own machine.

- **Single execution point.** A "Runtime Host" backend is the sole place agent
  execution happens; three surfaces (Desktop/Electron+React, a terminal UI/CLI, and an
  evaluation framework) all talk to the same backend rather than each embedding their
  own agent loop.
- **Durable, append-only execution log.** Every session is recorded as an append-only
  event log — model messages, tool calls, tool results, permission decisions, and
  termination events — with projections derived from it for the UI and for context
  management. This log is also the basis for crash recovery and (optionally) resuming
  a turn after interruption.
- **Sandboxed tool execution with a permission round trip.** Built-in tools (Read,
  Write, Edit, Bash, Glob, Grep) run in the agent process; anything requiring external
  access must get explicit user approval before it executes.
- **Stack:** Node.js 22+, TypeScript, SQLite, Electron+React for the desktop shell;
  npm-workspaces monorepo. Platform support today is macOS (primary) and an unsigned
  Windows preview; Linux is not yet supported.

## 2. Grimoire's current agent harness (for contrast)

- **Single execution point, but per-run, not a long-lived host.** The Hub spawns one
  short-lived child process per agent run through the
  `IAgentProcessLauncher`/`IAgentProcessHandle` port
  (`backend/src/Grimoire.Hub/AgentDispatch/IAgentProcessLauncher.cs`), a contract
  ADR-036 (Agent Child-Process Spawn Contract) formalizes. There is no persistent
  "workspace host" process analogous to Maka's Runtime Host — each ingest/query/lint
  run is a fresh process, terminated on completion.
- **Event channel, not an append-only session log.** Run progress streams out as
  NDJSON events on stdout (`started`/`heartbeat`/`activity`/`answer_chunk`/`completed`/
  `failed`), defined by ADR-037 (Agent Event Channel Protocol) and emitted via
  `backend/src/Grimoire.AgentRuntime/RunEvents/RunEventEmitter.cs`
  (event shapes in `backend/src/Grimoire.Hub/AgentDispatch/AgentRunEvent.cs`). Liveness
  is heartbeat-based (ADR-038); a persistent FIFO run queue survives Hub restarts
  (ADR-039). This is a live *stream*, one-directional (agent → Hub); it is not stored
  as a queryable, replayable append-only log of every message/tool-call/permission
  decision the way Maka's session log is.
- **Guardrails are in-harness, not a permission round trip.** Tool execution and
  deny-by-default policy evaluation happen inside the harness at one physical
  chokepoint, `GuardedToolExecutor`
  (`backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`, ADR-006,
  hardened further by ADR-034). There is no "agent asks, human approves in the moment"
  flow like Maka's `session/request_permission`: policy is evaluated deterministically
  against a versioned `SafetyPolicy`, denied actions are recorded with reasons, and the
  run continues with what remains allowed. This is a deliberate difference, not a gap —
  Grimoire's agents run unattended (Constitution Principle V), so a human-in-the-loop
  approval step per tool call does not fit the product.
- **Durable state is files, not a session-log projection.** Task artifacts (the
  per-run markdown record) and the wiki activity log are the durable record of what an
  agent did — the wiki log specifically is agent-authored and prepend-only (ADR-035),
  written by the agent through its own guarded tool call, not synthesized by the
  harness from an execution trace the way Maka's UI projections are derived from its
  event log.

## 3. Fit analysis — why Maka does not transfer directly

### 3.1 Different product: attended workspace vs. unattended harness

Maka's permission round trip and multi-surface (Desktop/CLI/Eval) design serve an
**interactive** workspace where a human watches and approves as the agent works.
Grimoire's agents (ingest/query/lint) run **unattended** against a fixed guardrail
policy decided in advance — there is no user present to approve a tool call mid-run.
Adopting Maka's permission-round-trip shape would mean building a UI-blocking human
gate into a harness whose entire design goal (Principle V) is that judgment lives in
the agent under versioned instructions, and containment lives in a deterministic,
unattended policy check. That is not a small delta; it is the opposite operating
model.

### 3.2 Stack mismatch

Maka is Node.js/TypeScript/Electron/SQLite; Grimoire's backend is .NET (per ADR-001),
with its own hexagonal port/adapter conventions (Constitution Principle I, ADR-010).
There is no shared runtime to embed Maka's Runtime Host into, and nothing in Maka is
packaged as a library rather than an application — it would have to be run as a
separate out-of-process service and integrated over some protocol Grimoire does not
currently have a port for. That is a new external-system boundary requiring its own
ADR (Principle I "New boundaries via ADR") for a payoff that is not yet established.

### 3.3 Governance cost with no identified pain point

Nothing in Grimoire's backlog currently names crash recovery, turn resumption, or a
replayable execution log as a requirement. Introducing either the concept or a
dependency to satisfy it now would be exactly the "assumed-upfront structural
boundary" Principle I rejects — earned via an ADR when a real need appears, not
speculatively.

## 4. Where Maka's ideas are still useful

In ascending order of invasiveness:

1. **Borrow the idea, not the code — now, no adoption.** Maka's append-only execution
   log (message/tool-call/tool-result/permission-decision/termination as one ordered
   record) is a clean reference shape if Grimoire ever needs to answer "exactly what
   did this run do, in order, replayably" more precisely than today's combination of
   NDJSON stream (ephemeral) + task artifact (agent-authored prose) + structured logs
   (Principle IV) currently allows. Worth re-reading if a future spec needs
   step-by-step run replay for debugging or audit.
2. **Crash recovery as a concrete reference.** Maka's stated crash-recovery and
   optional turn-resumption behavior is worth comparing against Grimoire's own
   heartbeat-based liveness detection (ADR-038) and persistent run queue (ADR-039) if
   partial-run recovery (resuming a truncated agent turn, rather than restarting it)
   ever becomes a requirement — today Grimoire's answer to a dead run is re-run from
   scratch via the queue, not resume-in-place.
3. **Not a candidate as a permission-approval model.** Unlike the two points above,
   Maka's interactive `session/request_permission` flow is explicitly the wrong shape
   for Grimoire's unattended guardrail model (§3.1) and should not be revisited absent
   a product decision to make some agent runs interactive/attended — which would be a
   significant reframing of Principle V, not an incremental change.

## 5. Recommendation

Do not build anything now: no current pain point is solved by Maka, its stack does not
fit the existing hexagonal boundary, and its core value proposition (interactive,
foreign-agent-friendly workspace) does not match Grimoire's unattended, single-purpose
agent model. Revisit this document if one of the following triggers fires:

- a spec needs precise, replayable, step-by-step run history beyond what the NDJSON
  event stream plus task artifact currently provide;
- partial-run recovery (resuming a truncated turn rather than re-running the whole
  task) becomes a real requirement;
- a product decision introduces an interactive/attended agent mode with human
  approval mid-run.

At that point, draft the ADR referenced in the header note and treat this document as
its decision context.

## Sources

- [apache/maka](https://github.com/apache/maka)
- Internal: ADR-001, ADR-006, ADR-034, ADR-035, ADR-036, ADR-037, ADR-038, ADR-039;
  `backend/src/Grimoire.Hub/AgentDispatch/IAgentProcessLauncher.cs`,
  `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`,
  `backend/src/Grimoire.AgentRuntime/RunEvents/RunEventEmitter.cs`
