# LLM-Wiki AI-Harness: Architecture & Development Guidelines

## Language Policy

**Primary Language: English**

All code, comments, documentation, and architectural artifacts must be written in English. This ensures:
- Consistency across codebase and documentation
- Accessibility for international teams and contributors
- Compatibility with LLM code generation (models trained primarily on English codebases)

Exception: Project-internal notes or personal development logs may use other languages if clearly marked, but all shared documentation, code comments, and specifications must be in English.

## Claude Code Project Context

### Project Constitution

The project's core principles are documented in: @.specify/memory/constitution.md

### Tech Stack

Reference: `docs/adr/` for detailed rationale on each decision.

## Spec-Driven Development (Spec Kit)

This project uses **Spec-Driven Development (SDD)** methodology with the GitHub Spec Kit toolkit.

### Available Commands

Use these slash commands within Claude Code to guide development:

- `/speckit-specify` — Create or update the detailed specification
- `/speckit-plan` — Break down the spec into a step-by-step implementation plan
- `/speckit-implement` — Implement the plan, creating code, tests, and documentation
- `/speckit-analyze` — Analyze the codebase for gaps, inconsistencies, or improvements
- `/speckit-clarify` — Clarify ambiguities in the specification
- `/speckit-constitution` — Create or refine project governing principles
- `/speckit-checklist` — Generate a domain-specific verification checklist
- `/speckit-tasks` — Generate tasks from the plan
- `/speckit-taskstoissues` — Convert tasks to GitHub issues

## Spec-Kit Workflow

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at `specs/002-agentic-ingest-core/plan.md`.
<!-- SPECKIT END -->
