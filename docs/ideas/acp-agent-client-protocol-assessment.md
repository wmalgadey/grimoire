# Assessment: Agent Client Protocol (ACP) for Grimoire Agent Management

> **Role of this document.** Decision context (source material), in the sense of the
> Document Map in `CLAUDE.md`: it is **not binding** for SDD. Its declared reader is
> the author of a future ADR that amends ADR-002 (agent execution model) or ADR-008
> (agent event channel) — e.g., when a remote/containerized agent transport or
> third-party agent integration is planned. Statements here become enforceable only
> once extracted into the constitution or an Accepted ADR.

**Date:** 2026-07-29
**Trigger:** Review of [block/buzz](https://github.com/block/buzz), which manages
agents via the Agent Client Protocol — question: is this useful for Grimoire, and
should it flow into our architecture?

**Verdict (TL;DR):** ACP is a well-designed protocol, but for Grimoire in its
current architecture it answers a question we have deliberately answered
differently. Do not adopt it now. Keep it as a candidate for a later transport
evolution — ADR-002 (deferred Option 3) and ADR-008 (transport-independent event
vocabulary) explicitly keep that door open.

---

## 1. What block/buzz does with ACP

[block/buzz](https://github.com/block/buzz) is a Rust-based collaboration
workspace (Nostr relay; humans and agents share channels). The relevant building
block is the `buzz-acp` crate: a harness that spawns **third-party, off-the-shelf
agents** (Goose, Claude Code, Codex) as subprocesses and talks to them via the
[Agent Client Protocol](https://agentclientprotocol.com/get-started/introduction)
— JSON-RPC 2.0 over stdio, originally published by Zed as an "LSP for coding
agents".

The buzz harness:

- listens for @-mentions on the relay and batches them into a single
  `session/prompt`;
- manages sessions and per-channel turn queues (at most one prompt per channel at
  a time), idle timeouts, wall-clock caps, heartbeats, and crash recovery;
- scales 1–32 agent subprocesses with lazy pool initialization;
- injects tool access (buzz CLI commands) into the agent environment, with
  per-agent keypairs as identity.

ACP itself provides: capability negotiation (`initialize`), multiple concurrent
sessions per connection, streamed `session/update` events, categorized tool calls
(read/edit/execute/…), and a `session/request_permission` flow in which the agent
asks the client for approval of sensitive actions.

> **Naming caveat:** there are two protocols abbreviated "ACP". buzz uses Zed's
> Agent *Client* Protocol, not IBM's Agent *Communication* Protocol.

## 2. Grimoire's current agent management (for contrast)

- **Custom in-process tool-use loop** in `Grimoire.AgentRuntime`
  (`Core/AgentLoop.cs`) against the Anthropic Messages API behind the
  `IModelClient` port. No external agent runtime — Claude Code CLI / Agent SDK
  subprocess was explicitly rejected in
  `specs/002-agentic-ingest-core/research.md` §R1 and ADR-006.
- **Process boundary:** the Hub spawns agent worker processes through the
  `IAgentProcessLauncher` / `IAgentProcessHandle` port
  (`Grimoire.Hub/AgentDispatch/IAgentProcessLauncher.cs`), production adapter
  `AgentProcessHost` (ADR-002, ADR-010 rule C4).
- **Transport:** one-way, event-shaped — CLI args + scoped env + one stdin write
  down; NDJSON run events (`started`/`heartbeat`/`activity`/`answer_chunk`/
  `completed`/`failed`) on stdout up (ADR-008). No bidirectional RPC, no
  permission round trip; cancellation is process-tree termination via
  `IAgentProcessHandle.Terminate()`.
- **Guardrails:** in-harness, at the single physical chokepoint
  (`GuardedToolExecutor`): registry check → path canonicalization →
  deny-by-default `SafetyPolicy` → write journal with rollback (ADR-006,
  Constitution Principle V).

## 3. Fit analysis — why ACP does not fit today

### 3.1 ACP's core value is interop with foreign agents — which we rejected

ACP pays off when arbitrary third-party agents (Claude Code, Goose, Codex) must
be plugged in behind one protocol. Grimoire's agents are our own C# code,
precisely so that guardrails live in harness code the conversation cannot touch
and harness contracts stay hermetically testable. As long as no third-party
agents are in scope, ACP's main value proposition does not apply.

### 3.2 ACP's permission model collides with Principle V / ADR-006

In ACP, the *agent* executes tools and asks the client for approval via
`session/request_permission`. Structurally this is the "hook-based post-hoc
filter" shape that research R1 and ADR-006 rejected. Grimoire's non-negotiable
rule is the inverse: tool execution and deny-by-default policy evaluation happen
*inside* the harness at the guarded-tool boundary. Adopting ACP wholesale would
require either overturning ADR-006 by a new ADR (not recommended) or using ACP
purely as a transport while leaving its permission flow unused.

### 3.3 Real governance cost

A new protocol is a new external-system boundary (Constitution Principle I,
ADR-010): it requires an ADR before implementation, a named port, an adapter
namespace, a containment rule with a Red/Green-probed architecture test, and a
JSON-RPC dependency that does not exist in `Directory.Packages.props` today
(Principle IV: new infrastructure/dependency territory). In addition,
`IAgentProcessHandle` is deliberately "stdout-lines-shaped"
(`ReadStdoutLinesAsync`); an ACP adapter is not a drop-in — the port would have
to be reshaped from "stdout lines" to "messages".

## 4. Where ACP (and buzz) are still useful

In ascending order of invasiveness:

1. **Borrow ideas now (no protocol adoption).** buzz's harness patterns are
   transferable independently of ACP: per-channel turn batching, idle/wall-clock
   caps, lazy pool initialization. ACP's design also shows what our event channel
   lacks *if it ever hurts*: a **bidirectional** channel with structured
   cancellation (today: kill the process tree) and session reuse instead of one
   process per run.
2. **ACP as a transport upgrade, later.** If concurrent ingest volume or
   remote/containerized agents cross the threshold that ADR-002 deferred
   (Option 3: separately-running daemon), ACP is a candidate for the "different
   byte transport" ADR-008 explicitly allows — as a new adapter under
   `Grimoire.Hub.AgentDispatch.Adapters.*`, with tools and guardrails staying
   harness-side and only `initialize` / `session/prompt` / `session/update`
   used. This needs an ADR that reshapes the port from stdout lines to messages
   while keeping ADR-008's event vocabulary and supervision semantics.
3. **ACP as a third-party-agent gateway, only with a new use case.** If Grimoire
   should ever admit external agents (e.g., a user's own Claude Code) into the
   wiki under control, ACP would be the right standard gateway — but that is a
   product feature with its own spec, and it would deliberately renegotiate the
   R1 decision, including how deny-by-default guardrails work in front of an
   agent whose tool execution we do not own.

## 5. Recommendation

Do not build anything now: no current pain point is solved by ACP, and adoption
would run against two Accepted ADRs (ADR-006, ADR-008) plus the constitution's
new-boundary rules. Revisit this assessment when one of the following triggers
fires:

- concurrent ingest volume justifies a daemonized agent runtime (ADR-002
  Option 3 threshold);
- a remote or containerized agent deployment is planned (ADR-008 "different byte
  transport" clause);
- a product decision introduces third-party agents.

At that point, draft the ADR referenced in the header note and treat this
document as its decision context.

## Sources

- [Agent Client Protocol — Introduction](https://agentclientprotocol.com/get-started/introduction)
- [agentclientprotocol/agent-client-protocol](https://github.com/agentclientprotocol/agent-client-protocol)
- [ACP: The LSP for AI Coding Agents (Marc Nuri)](https://blog.marcnuri.com/agent-client-protocol-acp-introduction)
- [block/buzz](https://github.com/block/buzz), crate `buzz-acp`
- Internal: ADR-002, ADR-006, ADR-008, ADR-010;
  `specs/002-agentic-ingest-core/research.md` §R1
