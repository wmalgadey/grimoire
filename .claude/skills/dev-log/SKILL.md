---
name: dev-log
description: Append this session's Spec-Kit/SDD learnings to dev-experience.md as a short, precise, first-person German entry. Use when the user asks to log insights, update the dev experience log, or wrap up a significant working session.
allowed-tools: Read, Edit
---

# dev-log — Update the Dev Experience Log

`dev-experience.md` (repository root) is the personal learning log of the Spec-Kit/SDD
journey. This skill appends the current session's insights to it. The log is outside
the SDD flow: never cite it in specs, plans, or ADRs.

## User Input

```text
$ARGUMENTS
```

You **MUST** consider the user input before proceeding (if not empty).

## Rules

- **Language**: German, first person ("ich"). The log is exempt from the project's
  English policy (clearly marked personal log).
- **Short and precise**: bullets over prose. A reader should get each lesson in
  seconds. Aim for well under 20 lines per entry.
- **Log process learnings, not work items**: insights about SDD, Spec-Kit, AI
  collaboration, and architecture governance. Litmus test: would this insight still
  matter on the next project, or does it change how I work? Routine implementation
  details do not qualify.
- **Never rewrite or reorganize older entries.** The log is append-only in spirit;
  only today's entry may be extended.

## Procedure

1. Read the tail of `dev-experience.md` (roughly the last 80 lines) to match the
   established format and avoid duplicating already-logged insights.
2. Determine today's date (`YYYY-MM-DD`).
3. Review the current session: which learnings are genuinely new relative to existing
   entries? Select at most a handful; prefer transferable insights over event recaps.
4. If a `## <today>: …` entry already exists, extend it in place. Otherwise append a
   new section: `## YYYY-MM-DD: <prägnanter Titel>`.
5. Use the log's established sub-structure only where it earns its place
   ("Was ich gemacht habe" / "Ergebnis" / "Erkenntnisse"). For a pure insight entry,
   an "Erkenntnisse" list alone is fine.
6. Do not commit unless the user asks.
7. Confirm to the user what was added, quoting the entry title.
