---
status: accepted
supersedes: ADR-011
---

# ADR-044: Shared Agent Runtime Library

> **Extends [ADR-010](ADR-010-hexagonal-ports-adapter-namespaces.md)**: the `IModelClient` port
> and its Anthropic adapter this ADR places at `Grimoire.AgentRuntime.Core`/
> `Grimoire.AgentRuntime.Core.Adapters.Anthropic` are the current entry for the port row ADR-010's
> table originally listed under `Grimoire.IngestAgent.AgentCore` — the same port-owned-by-consumer,
> adapter-one-level-below scheme ADR-010 decided, applied to this ADR's new consuming namespace.
> ADR-010's namespace scheme and containment rules C1–C5 are unchanged.

## Context and Problem Statement

Grimoire runs several specialized agents (Ingest, Query, Lint), each spawned as its own child
process (ADR-036) but all needing the same core machinery: a manual tool-use loop against a
model client, a guarded tool executor enforcing deny-by-default policy, fail-closed
instruction/policy loading, and the run-event emitter that feeds the agent event channel.
When the second agent arrived, the choice was whether each agent carries its own copy of that
machinery or whether it lives once in a shared library every agent host references. The
project owner directed one shared agent loop across agents, distinguished chiefly by system
prompt, with tools and policy free to differ per agent. This ADR restates that decision as
current truth: all agents run on the single shared runtime library `Grimoire.AgentRuntime`;
per-agent forks of loop, guardrail, or event-channel machinery do not exist.

## Decision Drivers

- One tested loop, model-client seam, and guarded tool executor for every agent — duplicated
  copies drift, and drift in guardrail code is a security defect, not just churn.
- Each agent must still differ where it legitimately differs: its tool registry, policy file,
  instruction files, and observability identities must never leak between agents.
- Principle I (hexagonal): the model-provider dependency needs a consumer-owned port with a
  contained adapter; Principle II: hermetic harness tests replace the model client with a
  fake — one port serves every agent.
- Every future agent must inherit the pattern by referencing the library and declaring its own
  registry/policy/prompts, not by copying code (the Lint agent later did exactly this).

## Considered Options

1. **Shared `Grimoire.AgentRuntime` class library** referenced by every agent host process;
   each host supplies its own tool registry, policy file, and system prompt.
2. Duplicate the loop/model-client/tool-executor/event-emitter code into each new agent
   project with no shared library.
3. Run later agents in-process inside the Hub, referencing the first agent's compiled core as
   a library, skipping process-per-run spawning.
4. Extend the first agent's executable with a mode flag that swaps tool registry, policy, and
   system-prompt path at runtime, instead of a second process/project.

## Decision Outcome

Chosen option: **Option 1 — one shared runtime library, `Grimoire.AgentRuntime`**, because it
gives every agent the same tested loop and guardrail machinery exactly once while keeping each
agent's authority (tools, policy, instructions) declared per agent, and because options 2–4
either invite guardrail drift (2) or collapse the per-process isolation and credential scoping
the spawn contract depends on (3, 4).

### What the runtime library owns

`Grimoire.AgentRuntime` is referenced by every agent host (`Grimoire.IngestAgent`,
`Grimoire.QueryAgent`, `Grimoire.LintAgent`) and owns the machinery that must exist exactly
once:

- `Grimoire.AgentRuntime.Core` — `AgentLoop` (the manual tool-use loop), the `IModelClient`
  port and its conversation/turn/tool-definition types. The port is consumer-owned here
  (Principle I); the production adapter `AnthropicModelClient` lives in
  `Grimoire.AgentRuntime.Core.Adapters.Anthropic`, the replay/capture adapters (ADR-012) in
  `Grimoire.AgentRuntime.Core.Adapters.Replay`, and hermetic tests use `FakeModelClient`.
- `Grimoire.AgentRuntime.Guardrails` — `GuardedToolExecutor`, `ToolRegistry` (the shared pool
  of tool definitions from which each agent declares its registry), `WriteJournal`,
  `DeniedActionRecord`, and the cross-process write coordination in its `Coordination`
  sub-namespace (mechanism owned by ADR-015).
- `Grimoire.AgentRuntime.RunEvents` — `RunEventEmitter`, the single implementation of the
  agent event channel's emitting side (protocol owned by ADR-037).
- `Grimoire.AgentRuntime.Instructions` — `SystemPromptLoader` and `PolicyLoader`, the
  fail-closed loaders for the ADR-007 instruction surface and the guardrail policy.

The library also carries the platform scaffold namespaces (`Telemetry`, `Composition`,
`Host` — telemetry bootstrap, model-client composition, the `AgentProfile`/`AgentHost`
startup template). That packaging shape — thin per-agent host executables over one platform
library, capabilities exactly as profiled — is ADR-013's aspect and is not re-decided here.

### What stays per-agent

An agent host contributes only what legitimately distinguishes it: its declared tool registry
(which of the shared tool definitions this agent registers — the registry's available contents
are governed by the ADR-006/ADR-030 lineage and per-agent write scopes by ADR-015/ADR-016/
ADR-031), its policy file and instruction files (ADR-007), its frozen observability
identities, and its intent-specific artifact handling. A tool name an agent's registry does
not declare is rejected as unknown even where a dispatch branch exists in shared code — this
is what keeps a runtime capability added for one agent invisible to the others.

### Boundary Rule

- **C6 (Boundary Rule)**: the Anthropic SDK is referenced only from
  `Grimoire.AgentRuntime.Core.Adapters.Anthropic`, and orchestration code must not reference
  the concrete `AnthropicModelClient` type outside adapter/composition namespaces. Enforced by
  the Red/Green-probed structural test `AgentRuntimeAdapterBoundaryRuleTests`.

### Consequences

- Good, because every agent shares one tested loop, model-client seam, and guarded tool
  executor — a guardrail fix or loop improvement lands for all agents at once.
- Good, because per-agent authority is declared, not coded: registries, policies, and prompts
  differ per agent while no agent-conditional logic exists in the shared machinery.
- Good, because a new agent inherits the pattern by referencing the library and declaring its
  own registry/policy/prompts — proven when the Lint agent arrived as exactly that.
- Bad, because extracting the library from the first agent's code was move-heavy refactor
  churn that broke `git blame` continuity; mitigated by move-only commits, and paid once.
- Neutral, because a shared library couples all agents to one version of the machinery —
  accepted deliberately: divergence between agents' core machinery is the failure mode this
  decision exists to prevent.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new agent host built on the shared runtime
  (its own registry, policy, instruction files, profile); a new runtime capability added once
  in the library and opted into per agent (as the streaming delta callback, the write
  coordination guard, and the retrieval tools each were); new tool definitions in the shared
  pool (ADR-006/ADR-030 lineage); new adapters behind the `IModelClient` port.
- **Invalidations (would require full supersession):** a per-agent fork or private
  reimplementation of loop, guardrail, event-emitter, or instruction-loading machinery;
  splitting the runtime into per-agent libraries; moving agent-conditional behavior into the
  shared machinery so that agents are distinguished by branching instead of declaration.

## More Information

Supersedes [ADR-011](ADR-011-query-agent-shared-runtime-and-concurrency-model.md), whose
runtime-sharing aspect this ADR restates as current truth; ADR-011's other aspects are
re-decided in [ADR-045](ADR-045-token-level-answer-streaming.md),
[ADR-046](ADR-046-query-dispatch-and-bounded-concurrency.md), and
[ADR-047](ADR-047-query-realtime-delivery.md).

Read alongside: [ADR-013](ADR-013-unified-agent-platform-packaging-and-naming.md) — platform
packaging, thin hosts, and the profile-declared capability guarantee;
[ADR-006](ADR-006-agent-tool-loop-guarded-boundary.md) and
[ADR-030](ADR-030-guarded-retrieval-tool-surface.md) — the guarded tool boundary and the
tool-registry contents; [ADR-015](ADR-015-query-write-scope-and-wiki-write-coordination.md) —
write scope and cross-process write coordination inside `Guardrails`;
[ADR-012](ADR-012-eval-runner-recorded-replay.md) — the replay/capture adapters;
[ADR-007](ADR-007-agent-instruction-surface.md) — the instruction surface the loaders serve;
[ADR-036](ADR-036-agent-child-process-spawn-contract.md) — the per-run child-process model the
hosts run under. None of their decisions are restated or narrowed here.
