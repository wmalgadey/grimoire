# LLM-Wiki AI-Harness: Architecture & Development Guidelines

## Language Policy

**Primary Language: English**

All code, comments, documentation, and architectural artifacts must be written in English. This ensures:
- Consistency across codebase and documentation
- Accessibility for international teams and contributors
- Compatibility with LLM code generation (models trained primarily on English codebases)

Exception: Project-internal notes or personal development logs may use other languages if clearly marked, but all shared documentation, code comments, and specifications must be in English.

**Verbatim user input is a record, not authored content.** A block that quotes what the
user actually said — the `**Input**: User description:` field that `/speckit-specify`
writes into `spec.md`, a quoted clarification answer, an issue excerpt — MUST be preserved
in the language the user used, unedited. Translating it destroys the traceability the field
exists for: it is evidence of the request, not a statement of the requirement. Everything
*derived* from it — every requirement, scenario, acceptance criterion, and all authored
prose around the quote — MUST be English, and that derived text is what contributors and
reviewers read. Flagging a non-English verbatim quote as a language-policy violation is a
false positive; the rule to apply to it is "is it marked as a quote and is everything
derived from it in English?"

## Document Map

The binding rules live in the constitution, imported into every session:
@.specify/memory/constitution.md

Every document has exactly one role. Binding statements flow one way:
source material → decision context → constitution/ADR → specs. A statement is only
enforceable once extracted into the constitution or an Accepted ADR.

| Artifact | Role | Binding for SDD? |
| --- | --- | --- |
| `.specify/memory/constitution.md` | Enforceable project rules | Yes — gates every plan |
| `docs/adr/` | Architectural decisions incl. tech-stack rationale (MADR) | Yes, once Accepted (Principle III) |
| `docs/decision-context-overview.md` | Problem space & product vision (North Star) | Only via extraction into constitution/ADRs; audited with `/drift-check` |
| `specs/<feature>/` | Feature-scoped SDD artifacts | Yes, for that feature |
| `docs/befunde-remediation-prompts.md` | Prompt library for remediation workflows | No — source material only; never cite as requirements in specs/plans/ADRs |
| `docs/foundational/llm-wiki-*.md`, `docs/ideas/project-conversation.md` | Source material (absorbed) | No — never cite as requirements |
| `docs/llm-wiki-pattern-conformance.md` | Conformance analysis: the agent instruction files against the LLM-Wiki pattern they came from | No — findings bind only once filed as issues and taken through the spec-kit workflow |
| `dev-experience.md` | Personal learning log (German) | No — outside SDD; never cite in specs/plans/ADRs; updated via `/dev-log` |

New documents require a declared reader (which process step consumes it?). If none
exists, the content belongs in `dev-experience.md`, not in a new file.

## Spec-Driven Development (Spec Kit)

This project uses **Spec-Driven Development (SDD)** with the GitHub Spec Kit toolkit.
All feature work MUST go through the spec-kit workflow — its mandatory order and gates
(specify → clarify → plan → ADR review → tasks → implement → converge) are defined in
the constitution's "Spec-Kit Workflow Integration" section. Do not implement features
ad hoc outside this workflow. The individual commands are available as auto-discovered
`/speckit-*` skills.

**Delivery shape.** A feature that spans several `tasks.md` phases SHOULD be delivered as
a stack of small pull requests rather than one big-bang PR — one layer per phase group,
each PR targeting the branch below it. This is a delivery convention, not an
architectural boundary: it needs no ADR and changes nothing about the SDD artifacts or
the Definition of Done, which stays whole-feature. The procedure lives in the
[`stacked-pr`](.claude/skills/stacked-pr/SKILL.md) skill.

**The delivery-shape decision happens between `/speckit-tasks` and `/speckit-implement`,
and it is made out loud.** The moment `tasks.md` exists, its phase groups are visible and
the cut points are knowable — that is the only point at which the choice is cheap. Before
starting implementation, state which shape this feature gets:

- **Stack** (default when `tasks.md` has more than two phase groups beyond Phase 0):
  invoke the `stacked-pr` skill and name the cut, then implement layer by layer.
- **Single PR**: say so and say why — a feature whose phases genuinely cannot be reviewed
  independently, or one small enough that a stack is ceremony.

**Writing the intended cut into `tasks.md` is not the same as delivering it.** Feature 024
recorded "a natural cut is Phase 0–2 / Phase 3 / Phases 4–7 / Phase 8–9" in its
Implementation Strategy section and then shipped all 64 tasks as one 72-file pull request:
the convention was known, the cut points were already identified, and the skill was never
invoked, because nothing in the workflow required stopping to ask. A recorded intent that
implementation does not act on is worse than no record — it reads like the decision was
made. If the answer is "single PR", the tasks.md Implementation Strategy section must say
that, not describe a stack nobody built.

Once implementation is under way this decision is effectively spent: retro-splitting a PR
that has already been reviewed discards the review rather than shortening it. Decide
before, not after.

## Spec-Kit Workflow

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
<!-- SPECKIT END -->
