---
status: accepted
---

# ADR-029: Harness Operator Turns Are Delimited Inside the User Channel

> **Amends [ADR-007](ADR-007-agent-instruction-surface.md)**: names a third kind of
> harness-authored text alongside the system prompt and the default user prompt — the
> *harness operator turn*, a steering message the loop sends mid-conversation — and gives
> it a delimiter of its own. Everything ADR-007 decided about the instruction surface
> (one `system-prompt.md` per agent, a versioned `default-user-prompt.md`, explicit CLI
> paths, a harness-owned scaffold no submission input can remove) stands unchanged; this
> adds the missing name for text the harness was already sending.

## Context and Problem Statement

`AgentLoop` builds its initial user message with explicit injection framing: source
content is wrapped in `<source>...</source>` and the agent is told, in the same message,
that what is inside is untrusted external data whose instructions it must not follow.

The harness then used that same `user` role for its own orders. When a turn stops on
`max_tokens` or `pause_turn` without calling a tool, the loop appended a bare, undelimited
`Continue the task.` — four words in the one channel the agent has just been instructed to
distrust, indistinguishable from a sentence a source document put there.

Nothing has gone wrong because of it: the payload is harmless, and no injection through
this path has been attempted. The defect is the shape of the boundary, not an observed
exploit — and the shape is what any future harness-side steering message would inherit
(GitHub issue [#126](https://github.com/wmalgadey/grimoire/issues/126)).

## Decision Drivers

- Principle V puts guardrails at the tool boundary and injection framing in the harness
  scaffold. A channel the harness tells the agent to distrust, and then uses to give
  orders, undercuts both.
- The Messages API has a purpose-built channel for this: a `{"role": "system"}` entry
  appended to `messages[]` — an operator-authority instruction mid-conversation that does
  not invalidate the cached prefix the way editing the top-level system prompt would.
- That channel is model-tier gated. It is supported on Opus 5, Opus 4.8, Fable 5 and
  Mythos 5, and **not** on `claude-haiku-4-5`, which
  [#117](https://github.com/wmalgadey/grimoire/issues/117) settled on as the deliberate
  default and floor. Deciding for the tiered channel today would mean shipping a path
  that does not run.
- Whatever the channel, three agents must be able to read it. Query and Lint assemble
  their own initial conversation and never send Ingest's scaffold, so a convention the
  scaffold alone introduces reaches one agent out of three.

## Considered Options

1. **A delimited marker block in the user turn**, self-describing, sent by the loop
2. A mid-conversation `{"role": "system"}` message, with a new port vocabulary for it
3. Leave the bare sentence and document the risk as accepted

## Decision Outcome

Chosen option: **Option 1.**

- Harness-authored text inside a `user` turn is delimited by
  `<harness-instruction>...</harness-instruction>`. That marker, and nothing else in a
  user turn, denotes text the harness wrote.
- The block is **self-describing**: it states, *inside the delimiters*, that its contents
  come from the harness rather than from a source document, so it carries its own meaning
  for callers that send no scaffold. It is not a convention the agent has to have been
  told about beforehand.
- **All** harness-authored steering text goes inside the marker — the explanation
  included. An explanation sitting beside the block would be undelimited harness prose in
  the user channel, which is the defect this decision removes, reintroduced one line down.
- The marker is harness-owned scaffold in the ADR-007 sense: it lives in harness code, no
  submission input can remove or alter it, and it is not part of any instruction file.
- The conversation vocabulary is unchanged — a marked block is still a `user` message —
  so `IModelClient` gains nothing and no adapter changes.
- The system-role channel is **deferred, not rejected**. It becomes the better option the
  moment the configured tier supports it; revisiting is tracked on #117, which carries
  the tier decision.

### Rule classification (Constitution Principle III)

This ADR introduces **no Boundary Rule** — no dependency direction, package containment,
or layering constraint. The rule it does introduce is a **Feature-Scoped Invariant**:

- *Harness-authored steering text reaching the agent is delimited and self-describing.*
  Verified by a classicist, state-based integration test that runs the loop through a
  continuation turn and asserts on the conversation the model client actually receives —
  never by reflecting over the constant or the type's shape. The test asserts both halves:
  what the harness said is inside the marker, and nothing outside it is anything but
  whitespace. It is scoped to the loop's
  current steering surface and is expected to be edited when that surface grows, which is
  a single-file amendment rather than a broken guard.

### Consequences

- Good, because the one channel the agent is told to distrust now has an identifiable
  operator lane inside it, and the next harness-side steering message inherits that shape
  instead of the bare sentence's.
- Good, because it runs on the configured model floor today, with no tier gate and no
  provider-side feature to wait on.
- Good, because the block is self-describing, so Ingest, Query and Lint get the same
  guarantee without three scaffolds agreeing.
- Bad, because every continuation turn now carries a short explanatory paragraph it did
  not before. That is a real per-turn token cost on the continuation path, accepted as
  the price of the marker meaning something to an agent that was never told about it.
- Bad, because a delimiter inside untrusted-adjacent text is a weaker guarantee than a
  separate role: a source document can write the marker too. It is a clear improvement on
  an undelimited sentence and explicitly not a claim of unforgeability — the durable fix
  is the system-role channel, deferred above.
- Neutral, because the `<source>` framing paragraph is untouched. Introducing the marker
  there as well would invalidate every recorded eval replay (ADR-012) and is left as a
  follow-up to be made together with a recording refresh.

## More Information

Option 2 is the one the API is designed for and the one this decision expects to adopt
later; it is deferred purely on the tier gate, and it also needs conversation vocabulary
for a role the port does not model today. Option 3 was rejected because the cost of the
current shape is not the present payload — it is that it is the default every future
steering message inherits.

Implementation: `Grimoire.AgentRuntime.Core.AgentLoop` (`HarnessInstructionTag` and the
continuation message it wraps).
