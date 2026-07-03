---
status: accepted
---

# ADR-003: Domain vs. Operational State Persistence

## Context and Problem Statement

Grimoire persists two materially different kinds of state. Domain state — wiki pages,
task artifacts, the wiki index, and the append-only operation log — is the user-facing
knowledge base itself: it must stay portable, human-readable, diffable, and editable
outside Grimoire entirely (e.g. in Obsidian or any Markdown editor), and benefits from
version control's built-in audit trail and rollback. Operational state — specifically,
which task is currently running — is internal bookkeeping: it must survive an unplanned
Hub restart (so an interrupted task doesn't get silently forgotten and left stuck
"running" forever) but is not itself something a user browses or edits directly.
Treating both categories the same way fails in both directions: storing operational
bookkeeping in git would pollute the knowledge base with internal noise, while storing
domain knowledge in a database would break the portability and external editability that
domain state requires.

## Decision Drivers

- Domain state must remain plain-file, git-diffable, and free of opaque/binary formats.
- Operational state must survive an unplanned Hub restart without being committed to git
  as internal noise.
- No new infrastructure or service should be introduced beyond what one developer can
  reasonably operate alone.

## Considered Options

1. Git-tracked markdown files for domain state + embedded SQLite file for operational state
2. Everything in a relational database (including wiki content)
3. Everything as git-tracked markdown files (including in-flight task status)

## Decision Outcome

Chosen option: **Option 1.**

- Domain state — wiki pages, task artifacts (once finalized), `index.md`, `log.md` — are
  plain markdown files, committed to the project's git repository by the agent that
  produced them.
- Operational state — specifically, which task is currently in a non-terminal status —
  lives in a single embedded SQLite file owned by the Hub (outside git, e.g. under a
  `.grimoire/` runtime data directory). On Hub startup, any task recorded as "running" in
  SQLite with no corresponding live process is reconciled to "failed" with an
  interruption reason, and the task artifact file is updated to match.

### Consequences

- Good, because domain content stays fully Obsidian-compatible and diffable with zero
  extra tooling.
- Good, because SQLite requires no separate server process, satisfying the goal of
  minimal operational overhead while still surviving process restarts (a plain in-memory
  store would not).
- Bad, because the Hub now depends on two persistence mechanisms instead of one; accepted
  because the two state categories have genuinely different lifecycle and durability
  requirements that a single mechanism cannot satisfy for both.

## More Information

This split — git-tracked plain files for anything the user should be able to read or
edit directly, a small embedded store for anything that is purely internal runtime
bookkeeping — is the general persistence pattern for the project, not specific to any
one feature.
