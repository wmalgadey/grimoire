# LLM-Wiki AI-Harness: Architecture & Development Guidelines

## Language Policy

**Primary Language: English**

All code, comments, documentation, and architectural artifacts must be written in English. This ensures:
- Consistency across codebase and documentation
- Accessibility for international teams and contributors
- Compatibility with LLM code generation (models trained primarily on English codebases)

Exception: Project-internal notes or personal development logs may use other languages if clearly marked, but all shared documentation, code comments, and specifications must be in English.

**Verbatim user input is a record, not authored content.** A block that quotes what the user actually said — the `**Input**: User description:` field that `/speckit-specify` writes into `spec.md`, a quoted clarification answer, an issue excerpt — MUST be preserved in the language the user used, unedited. Translating it destroys the traceability the field exists for: it is evidence of the request, not a statement of the requirement. Everything *derived* from it — every requirement, scenario, acceptance criterion, and all authored prose around the quote — MUST be English, and that derived text is what contributors and reviewers read. Flagging a non-English verbatim quote as a language-policy violation is a false positive; the rule to apply to it is "is it marked as a quote and is everything derived from it in English?"

## Markdown Formatting

**Wrap long paragraphs for diff readability, but treat the width as an orientation, not a hard
rule.** A paragraph written as one very long line is hard to review in a `git diff` or a terminal
— the whole paragraph shows as a single changed line and reading it means scrolling horizontally.
Wrap prose, rationale blocks, and prose list items to roughly 90-100 characters per line so diffs
stay reviewable. This is guidance, not an enforced cap: never fracture a phrase mid-word or
mid-clause just to hit the character count, and let a line run a little long rather than force
an awkward break. Only break where markdown itself requires it otherwise: between paragraphs (a
blank line), between list items, before/after headings, and inside fenced code blocks — a
wrapped line inside a paragraph stays part of that paragraph; it is never a paragraph boundary
and must not be read or generated as one.

Weigh the trade-offs for the content at hand rather than applying this everywhere uniformly:
wrapping helps diff review, but it can still split a `grep`/search match across two lines, and
text copied or piped elsewhere can pick up the embedded newlines mid-sentence. For content that is
dense with material that gets copied, grepped, or piped between tools — task artifacts, log
entries, generated records, and verbatim-quoted user input — prefer a single unwrapped line per
paragraph instead, since those failure modes matter more there than diff-scrolling does. Apply
this to every markdown file in the repository — the constitution, ADRs, specs, plans, this file,
and any other authored `.md` content — not just newly written ones; reflow a file's touched
paragraphs to whichever convention fits its content when you next edit it for other reasons,
rather than leaving an arbitrary mix.

## Document Map

The binding rules live in the constitution, imported into every session:
@.specify/memory/constitution.md

Every document has exactly one role. Binding statements flow one way:
source material → decision context → constitution/ADR → specs. A statement is only
enforceable once extracted into the constitution or an Accepted ADR.

| Artifact | Role | Binding for SDD? |
| --- | --- | --- |
| `.specify/memory/constitution.md` | Enforceable project rules | Yes — gates every plan |
| `specs/<feature>/` | Feature-scoped SDD artifacts | Yes, for that feature only |
| `docs/adr/` | Architectural decisions incl. tech-stack rationale (MADR) | Yes, once Accepted (Principle III) |
| `docs/foundational/decision-context-overview.md` | Problem space & product vision (North Star) | Only via extraction into constitution/ADRs; audited with `/drift-check` |
| `docs/foundational/llm-wiki-*.md`, `docs/ideas/*.md` | Source material (absorbed or moved to GitHub issues) | No — never cite as requirements |
| `docs/**/*.md` (only where no row above matches) | Everything else under `docs/`: analyses, problem summaries, conventions, operational references | No — never cite as requirements |
| `dev-experience.md` | Personal learning log (German) | No — outside SDD; never cite in specs/plans/ADRs; updated via `/dev-log` |

The last row is a catch-all: where a file matches it *and* a row above, the more specific
row wins. An ADR under `docs/adr/` stays binding once Accepted — the catch-all never
downgrades it.

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

**A requirements change discovered mid-stack still goes through `/speckit-clarify` on the
spec layer — never as a direct edit on a downstream layer.** Planning or tasks work
routinely surfaces a real problem with what the spec asks for (an eval criterion that
implies a corpus far larger than anything else in the repo, a success criterion nothing
downstream can actually gate on). The fix belongs in the spec, and the *only* sanctioned
way to change what a spec requires post-creation is `/speckit-clarify`, run on the branch
that owns `spec.md` — not a direct edit made in reaction to the planning feedback, however
correct the resulting text is, and never a patch applied only to `plan.md`/`tasks.md` on a
layer above while `spec.md` still describes the old requirement. Feature 026 did this
wrong once: a plan-phase finding was pushed straight into `spec.md` as a raw edit, which
made spec.md and the layers above agree with each other but skipped the mechanism that
makes a requirements change visible as a decision (a dated `## Clarifications` entry,
Recommended/options framing, a checklist re-validation) rather than an untracked rewrite.
Once the spec layer is corrected through `/speckit-clarify`, every layer above it is
rebased onto the new spec commit and its own `/speckit-plan` or `/speckit-tasks` output is
regenerated against that corrected base — not hand-patched to match. A stack's whole point
is that each layer is honest about what it was built from; a layer whose plan or tasks
quietly know something the spec doesn't say yet defeats that.

**Between features: triage the board.** When a feature reaches its Definition of Done and
its final PR merges, run the [`issue-triage`](.claude/skills/issue-triage/SKILL.md) skill
before choosing the next unit of work. The pinned triage map issue is the durable state;
the skill's label taxonomy (`quick-fix`, `decision-needed`, `blocked`) applies at issue
creation time too, so sessions that file issues label them on filing.

## Spec-Kit Workflow

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at specs/025-agent-owned-log/plan.md
<!-- SPECKIT END -->
