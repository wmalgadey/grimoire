# LLM-Wiki AI-Harness: Architecture & Development Guidelines

## Language Policy

**Primary Language: English**

All code, comments, documentation, and architectural artifacts must be written in English. This ensures:
- Consistency across codebase and documentation
- Accessibility for international teams and contributors
- Compatibility with LLM code generation (models trained primarily on English codebases)

Exception: Project-internal notes or personal development logs may use other languages if clearly marked, but all shared documentation, code comments, and specifications must be in English.

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
| `docs/llm-wiki-*.md`, `docs/project-conversation.md` | Source material (absorbed) | No — never cite as requirements |
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

## Spec-Kit Workflow

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at specs/002-agentic-ingest-core/plan.md
<!-- SPECKIT END -->
