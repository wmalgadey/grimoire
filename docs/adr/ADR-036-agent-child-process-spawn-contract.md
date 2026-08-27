---
status: accepted
supersedes: ADR-002
---

# ADR-036: Agent Child-Process Spawn Contract

## Context and Problem Statement

Grimoire's Hub dispatches work to specialized agents (Ingest, Query, Lint), each of which
runs its own LLM-based processing pipeline with its own runtime and dependency chain. How
the Hub and an agent are wired together — in-process call, spawned process, or remote
service — determines whether agents can be run, scaled, and eventually containerized
independently of the Hub, and whether a crashed agent can corrupt Hub state. ADR-002 fixed
this execution model when the first agent was built; since then, later decisions moved
result reporting off the exit code, narrowed the launch mechanism to build-distributed
artifacts, and added bounded re-entry — each recorded as a partial amendment. This ADR
restates the spawn contract as a single current-truth decision: each agent run executes as
a standalone child process of the Hub, parameterized entirely at spawn time.

## Decision Drivers

- Agents must remain independently containerizable later without a rewrite of their
  interface to the Hub — the spawn contract must be the same contract a container would use.
- Operational overhead must stay proportional to a solo-operator project: no message
  broker, job-queue service, or network/auth surface between Hub and agent.
- A crashed or killed agent must leave no in-process Hub state to corrupt; recovery must
  reduce to inspecting durable files and operational state, not shared memory.
- The Hub must hold no agent business logic in-process (Constitution Principle V: the
  harness orchestrates and constrains, agents decide wiki content).

## Considered Options

1. Each agent as a standalone .NET console app, invoked by the Hub as a child process
   per run
2. Agents as in-process libraries called directly by the Hub
3. Agents as separately-running daemons, called over HTTP/gRPC

## Decision Outcome

Chosen option: **Option 1 — standalone console app, spawned as a child process per run**,
because it gives each agent an independent lifecycle and failure domain at zero
infrastructure cost, and its parameter contract survives a later move into a container as
a deployment change rather than a redesign.

- **One process per run, parameterized entirely at spawn time.** For each accepted run the
  Hub spawns the agent worker as a child process. Everything the run needs is supplied at
  launch: the task, target/source reference, and repository paths (wiki, tasks, index, log
  locations) travel as CLI arguments; credentials travel in a scoped environment block
  composed per ADR-004. Nothing is negotiated with the agent after spawn — the process
  either runs the parameterized task or fails.
- **Launch source and mode.** The Hub launches each agent from its build-distributed
  artifact directory in the single launch mode ADR-043 defines
  (`dotnet <AgentDir>/<agent-id>/Grimoire.<Type>Agent.dll`). The Hub consumes build
  artifacts and never produces them; launch-mode and artifact-layout rules are owned by
  ADR-043 and not restated here.
- **File-based durable output; agent-owned task artifact.** The agent performs all wiki
  reads/writes against the working tree directly (through its guarded tool boundary,
  ADR-006) and creates and maintains its own task artifact — the persistent file recording
  what it did — throughout its lifecycle, from start to final success or failure. The Hub
  does not author agent narrative output.
- **Result reporting is the event channel, not the exit code.** The Hub starts the agent
  without blocking and learns the run's progress and outcome through the agent event
  channel (ADR-037), with failure detected by heartbeat/liveness supervision (ADR-038).
  The process exit code is not awaited and is not part of the Hub↔agent result contract;
  agents still set it for manual CLI invocation and diagnostics only.
- **Boundary Rule — dispatch-only relationship.** `Grimoire.Hub` MUST NOT reference any
  agent assembly; the only permitted relationship is spawning the agent worker as a child
  process. Enforced by the existing Red/Green-probed structural test
  `HubAgentDispatchBoundaryRuleTests`. This rule is what keeps the agent artifact
  directory, not the Hub's output, the home of the agent runtime (ADR-043).
- Which code sites may construct a process, and the ArgumentList-only construction rule at
  those sites, are owned by ADR-034 and not re-decided here. Retry, backoff, and re-entry
  of a run under the same task id are owned by ADR-025.

### Consequences

- Good, because the spawn contract (arguments and scoped environment in; task artifact,
  wiki files, and events out) is the same contract a future container would use —
  containerizing later is a deployment change, not a redesign.
- Good, because a crashed or killed agent process leaves no in-process Hub state to
  corrupt; supervision (ADR-038) and re-entry (ADR-025) operate purely on process
  boundaries, durable files, and operational state.
- Good, because the parent↔child relationship needs no network surface, no auth, and no
  broker — hermetic harness tests exercise dispatch with a fake agent executable.
- Bad, because per-run process spawn pays process-start cost on every run; acceptable at
  single-operator volumes, and bounded because the Hub launches prebuilt artifacts
  (ADR-043) rather than compiling anything at dispatch time.
- Neutral, because daemon/queue-based execution (Option 3) is deferred, not rejected — it
  remains available if concurrent agent volume ever justifies its operational cost.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new agent type spawned under the same
  contract (a new worker artifact, its own CLI parameterization, its own scoped
  environment); new CLI parameters or environment entries added within the
  parameterize-at-spawn model; a new consumer of the dispatch path.
- **Invalidations (would require full supersession):** moving agent execution in-process
  into the Hub; long-lived agent daemon processes serving multiple runs instead of one
  process per run; container-based execution replacing child-process spawning as the
  primary execution model; parameterizing runs through a post-spawn negotiation channel
  instead of spawn-time arguments and environment.

## More Information

Supersedes [ADR-002](ADR-002-ingest-agent-execution-model.md), folding in the amendments
that touched its aspect: result reporting via events instead of an awaited exit code
(originally ADR-008, now [ADR-037](ADR-037-agent-event-channel-protocol.md) /
[ADR-038](ADR-038-heartbeat-run-supervision.md)) and the single launch mode from
build-distributed artifacts (originally ADR-022, now
[ADR-043](ADR-043-build-distributed-agent-artifacts.md)).

Read alongside: [ADR-004](ADR-004-credential-scoping.md) — credential injection into the
child environment; [ADR-034](ADR-034-path-and-subprocess-containment-hardening.md) —
spawn-site allowlist and ArgumentList-only construction;
[ADR-025](ADR-025-ingest-task-lifecycle-reentry.md) — liveness reactivation, manual
restart, and status history; [ADR-006](ADR-006-agent-tool-loop-guarded-boundary.md) — the
guarded tool boundary inside the agent process;
[ADR-013](ADR-013-unified-agent-platform-packaging-and-naming.md) — agent packaging and
naming. None of their decisions are restated or narrowed here.
