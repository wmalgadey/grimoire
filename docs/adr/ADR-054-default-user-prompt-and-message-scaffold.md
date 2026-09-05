---
status: proposed
supersedes: ADR-007
---

# ADR-054: Per-Run Steering Is a Versioned Default User Prompt Inside a Harness-Owned Scaffold

> **Status notes** (informational, no status change):
> - Extends [ADR-043](ADR-043-build-distributed-agent-artifacts.md): the default-user-prompt document
>   is delivered by the agent build, in the location that ADR already decided.
> - Related: [ADR-029](ADR-029-harness-operator-turn-delimiter.md), which delimits mid-conversation
>   harness operator turns inside the same user channel this ADR's scaffold owns.

## Context and Problem Statement

[ADR-007](ADR-007-agent-instruction-surface.md) decided two things at once: that an agent's system
prompt is a single file, and that per-run steering text is a versioned `default-user-prompt.md`
wrapped by a harness-owned message scaffold. Its first aspect is being invalidated —
[ADR-053](ADR-053-agent-system-prompt-composition.md) composes the system prompt from two documents —
and Constitution Principle III's whole-ADR supersession rule allows no partial retirement: ADR-007
goes to Superseded as a whole, and every aspect of it that is still valid must be re-decided as its
own independent, single-aspect ADR rather than inherited by reference.

This ADR is that re-decision for the user-channel aspect. Its substance is unchanged from ADR-007;
what changes is that it now stands on its own, so a reader of the current ADR set finds the rule
without having to reconstruct it from a superseded document.

## Decision Drivers

- Changing default steering text must remain an instruction-file change with git history, never a
  backend release (Constitution Principle V's boundary smell test).
- The Hub must be able to display the steering default in a submission form without duplicating its
  text in backend or frontend code.
- No per-run submission input may remove or alter the harness's own message framing — the task/source
  header, the `<source>` delimiters, the untrusted-data framing — or guardrail policy (ADR-006).
- Not every agent has per-run steering: query takes its prompt per turn, lint takes none at all, so
  the document must be required only where the agent's profile declares it.

## Considered Options

1. **Re-decide ADR-007's user-channel rules unchanged, as their own ADR.**
2. Fold these rules into ADR-053 so one ADR covers the whole instruction surface again.
3. Drop the default-user-prompt document and hardcode steering text in the backend again.

## Decision Outcome

Chosen option: **Option 1** — the rules are re-stated here, unchanged in substance, as a single
aspect: how per-run steering text reaches the agent.

- **`default-user-prompt.md`** is the versioned default per-run steering text for any agent whose
  profile declares it. It is delivered by the agent build alongside the agent's other instruction
  documents, loaded verbatim and fail-closed (missing, unreadable or empty ⇒ the run fails before any
  write) whenever it is the effective source.
- **Per-run override**: the harness accepts a bounded-length override for one run. The effective
  prompt — override or default — is what the agent receives, and which of the two it was is recorded
  with the run.
- **The message scaffold stays harness-owned**: the task/source header, the `<source>` delimiters and
  the untrusted-data framing always wrap the effective user prompt. No submission input can remove,
  reorder or reach outside it.
- **The agent CLI takes explicit paths** (`--default-user-prompt-path`, optional `--user-prompt`);
  the agent performs no discovery of its own.
- **Feature-Scoped Invariant** (Principle III): "an override replaces the default's text and nothing
  else — the scaffold around it is identical either way", verified by a classicist behavioral test
  that submits an override and asserts the resulting message, never by reflection.
- This ADR introduces **no Boundary Rule**. The instruction-authorship rule that covers
  `default-user-prompt.md` as a write target is owned by ADR-053, which carries its literal set.

### Consequences

- Good, because steering defaults stay reviewable, versioned instruction files rather than code.
- Good, because the current ADR set states the rule directly instead of pointing at a superseded ADR.
- Neutral, because nothing about the running system changes: this is a re-decision at the same
  substance, forced by the whole-ADR supersession rule, not a new decision.
- Bad, because the instruction surface is now described by two ADRs where one used to do, so a reader
  needs both. Mitigated by the cross-references here and in ADR-053, and by the fact that the split
  is exactly the single-aspect boundary the constitution asks for.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new agent profile that declares (or omits) the
  default-user-prompt document; changes to the *content* of a default-user-prompt document; a new
  harness-authored element inside the scaffold that no submission input can alter.
- **Invalidations (would require full supersession):** steering defaults moving back into backend
  code; a submission input that can alter or remove the scaffold or the guardrail policy; per-run
  steering reaching the agent through the system prompt instead of the user channel.

## More Information

Supersedes [ADR-007](ADR-007-agent-instruction-surface.md) whole, jointly with
[ADR-053](ADR-053-agent-system-prompt-composition.md), which re-decides ADR-007's system-prompt aspect
and explains why the supersession was required.
