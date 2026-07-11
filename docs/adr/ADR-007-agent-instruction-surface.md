---
status: accepted
---

# ADR-007: Agent Instruction Surface — Single System Prompt and Versioned Default User Prompt

## Context and Problem Statement

The Ingest agent's instructions currently live in `agents/ingest/CLAUDE.md` plus
auto-discovered `agents/ingest/skills/*/SKILL.md` files, all concatenated verbatim into
one system prompt at run start. The layout mimics Claude Code conventions (progressive
disclosure, on-demand skill loading) that the runtime does not implement, giving
maintainers a false mental model of how instructions reach the agent. Separately, the
per-run steering text (the "user prompt") is hardcoded in backend code, so changing
default steering — a wiki-behavior change under Constitution Principle V — requires a
backend release, and feature 004 needs the Hub to display and override that text per
submission. How agent instructions are laid out, loaded, and surfaced to other
components is a cross-cutting concern every future agent (Query, Lint) will inherit, so
it must be fixed by ADR rather than decided implicitly inside one feature.

## Decision Drivers

- The instruction artifact must be truthful: its name and layout must match how the
  runtime actually consumes it (Principle V: instruction files must genuinely govern
  the agent's context).
- Changing default steering text must be an instruction-file change, not a backend
  change (Principle V boundary smell test).
- The Hub must be able to display instruction defaults (feature 004's submission form)
  without duplicating their content in backend or frontend code.
- Fail-closed loading, SHA-256 traceability per run, and the ADR-002 child-process CLI
  contract must be preserved.
- Per-run user overrides must never be able to alter the harness-owned message scaffold
  (source delimiters, injection framing) or guardrail policy (ADR-006).

## Considered Options

1. **Single `system-prompt.md` + versioned `default-user-prompt.md` per agent**, loaded
   by path; harness composes the final message around the effective user prompt
2. Keep the `CLAUDE.md` + `skills/*/SKILL.md` layout and document that it is
   concatenation-only
3. Rename to a multi-file layout (e.g. `operating-rules.md` + `conventions.md`) that is
   still concatenated
4. Implement real progressive skill loading so the current layout becomes truthful

## Decision Outcome

Chosen option: **Option 1.**

Per agent, the instruction surface is:

- `agents/<agent>/system-prompt.md` — the entire system prompt, one file, loaded
  verbatim, fail-closed (missing/unreadable/empty ⇒ run fails before any write),
  SHA-256 recorded in the task artifact.
- `agents/<agent>/default-user-prompt.md` — the default per-run steering text. The
  harness (Hub) may read it for display and accept a per-run override within a bounded
  length; the agent process receives the effective prompt and records it in the task
  artifact. Fallback loading is fail-closed like the system prompt.
- The message scaffold (task/source header, `<source>` delimiters, untrusted-data
  framing) stays in harness code and always wraps the effective prompt; no submission
  input can remove it.
- Agent CLI takes explicit paths (`--system-prompt-path`,
  `--default-user-prompt-path`, optional `--user-prompt` override) instead of an
  instructions directory scan.

For the Ingest agent, `CLAUDE.md` and `skills/wiki-maintenance/SKILL.md` are merged
into `system-prompt.md` and deleted.

### Consequences

- Good, because the artifact and the mechanism are now identical — no implied skill
  mechanics that don't exist; editing one file provably edits the whole system prompt.
- Good, because steering defaults become versioned, reviewable instruction files;
  changing them is a content change with git history, not a backend release.
- Good, because the Hub reads instruction defaults from the same file the agent uses —
  single source of truth for UI display, no drift.
- Bad, because a future need for genuinely modular/conditional instruction loading
  (true skills) would require revisiting this ADR; explicitly deferred until an agent
  actually needs context-dependent instruction subsets (Option 4 rejected as
  speculative machinery today).
- Neutral, because task-artifact recording keeps the existing list shape
  (`instruction_files`) with one entry, so 002-era artifact readers are unaffected.

## More Information

Rejected options: Option 2 keeps the misleading mental model that motivated this
decision. Option 3 repeats it one level down — a structural split with no runtime
meaning. Option 4 builds loading machinery no current feature needs and would still
leave the default-steering-in-code problem unsolved.

This pattern applies to every future agent (Query, Lint): one system-prompt document,
one default-user-prompt document, explicit CLI paths, harness-owned scaffold.
Structural enforcement per Principle III: the existing instruction-context integration
tests are updated to assert single-document loading and fail-closed behavior; the
guarded-write boundary test (ADR-006) is unaffected.
