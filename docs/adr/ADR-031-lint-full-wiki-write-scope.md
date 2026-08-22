---
status: accepted
---

# ADR-031: Lint Holds Full Authority Over Wiki Content, in Both Modes

> **Supersedes [ADR-016](ADR-016-lint-write-scope-frontmatter-only-enforcement.md)**:
> ADR-016's `frontmatter-only` write scope for Lint is removed, not narrowed. The
> `FrontmatterOnly` write *mode* it introduced into the policy model is retained (see R5);
> what is superseded is the decision that Lint runs under it.
>
> **Amends [ADR-018](ADR-018-remediation-action-authorization-and-execution.md)**: human
> authorization remains the gate on whether a proposed remediation *runs*. It is no longer
> also the moment the agent acquires write authority — the authorization workflow and its
> state machine are otherwise unchanged.
>
> **Amends [ADR-017](ADR-017-log-and-catalog-entry-format-enforcement.md)**: Lint may now
> write `index.md` and `log.md`, so ADR-017's format enforcement (and ADR-028's prepend
> ordering) binds Lint's writes to those files exactly as it binds any other agent's. The
> format rules themselves are unchanged.

## Context and Problem Statement

Lint's policy declares one write rule: `frontmatter-only` on the content root. That policy
file is passed by all three coordinators — `LintRunCoordinator`, `RemediationRunCoordinator`
and `RemediationMessageTurnCoordinator` all pass `_paths.Lint.PolicyPath` — so the survey run
and the execution of a human-authorized remediation run under identical, equally narrow scope.

The visible consequence is a dead end: a remediation proposal whose fix needs a body edit is
denied at the tool boundary *after* a human has authorized it (#64, #159). The structural
consequence is that the scope cannot be widened for execution without also widening the survey.

Feature 026's clarification settled both, and went further than the issue proposed. Recorded
verbatim as the decision input: *"der lint agent hat vollen zugriff auf das wiki (der
frontmatter only zugriff war nur ein implementierungsdetail auf dem weg zu Verision 1.0). der
lint agent kann änderungen auch schon durchführen wenn er es für notwendig sieht. dinge, die er
dem benutzer als \"aktion\" überlässt sollen als remediation tasks im ui angezeigt werden. […]
we still have git as a major safety net"*

Two things follow that ADR-016 assumed the opposite of. `frontmatter-only` was scaffolding on
the way to 1.0, not a designed boundary. And a remediation task is what the agent raises when
it decides to leave an action to the user — not a permission it must obtain before acting.

## Decision Drivers

- Constitution Principle V: the wiki is maintained by agent judgment. A mechanical limit on
  *what kind of edit* is permissible is the harness deciding wiki content by proxy.
- The remediation loop is incomplete while an authorized fix can be denied.
- ADR-016's own justification was that spec 013's SC-002 placed frontmatter-only in the
  deterministic tier. That success criterion belonged to a feature whose scope has now been
  deliberately changed; the enforcement mechanism outlived the requirement.
- Git history is the recovery path for destructive change, which is what makes a wide scope
  tolerable without a harness-side confirmation or soft-delete mechanism.
- Deletion is the one action whose mistake destroys rather than changes, so it must not be
  acquired implicitly by any other agent.

## Considered Options

1. **One scope, full authority over the content root, both modes.**
2. Two scopes — frontmatter-only survey, wider execution — as two policy artifacts.
3. One artifact declaring both scopes, selected by run mode.
4. A base policy plus an execution overlay permitted only to widen writes.

## Decision Outcome

Chosen option: **1.** Options 2–4 all encode the premise that human authorization is a
permission boundary, which the decision above rejects.

### R1 — One policy, one scope (Boundary Rule)

`Grimoire.LintAgent/Instructions/policy.json` declares a single write rule of mode
`read-write` on the content root, with no `excludePrefixes`. All three coordinators keep
passing the same path. **No per-mode policy file, mode selector, or scope overlay is
introduced** — the absence of a split is the decision, not an unimplemented detail. The
harness MUST NOT branch on run mode when deciding what a write may touch.

### R2 — `index.md` and `log.md` are in scope (Boundary Rule)

Lint's exclusion of the two reserved files is removed. An agent that can create and delete
pages must be able to keep the catalog honest and record what it did; excluding it would mean
Lint could delete a page it could not un-list, with the drift caused by the one agent
forbidden to fix it. Ingest already writes both files, so this grants no capability the
harness has not already proven.

Whether the index agrees with the page set is **not** a harness invariant. Reconciling them is
within the agent's power and therefore within its judgment (Principle V).

### R3 — Deletion is a distinct, explicitly granted capability (Boundary Rule)

A `delete_file` tool is added, mirroring `rm` per ADR-030's shell-shaped rule. Deletion is
**not** evaluated as a write. The policy gains a separate `delete` scope, deny-by-default, and
only Lint's policy declares it.

This distinction is load-bearing. Ingest's policy already declares `read-write` on the content
root; had deletion been folded into the write scope, Ingest would have silently acquired the
ability to delete every page in the wiki as a side effect of this feature. No agent may gain
deletion by inheritance.

### R4 — A deletion is journaled (Boundary Rule)

`WriteJournal` records the deleted path and its content, so a run that deletes and then fails
restores it in the same reverse-order rollback that restores an overwrite (ADR-006). Rollback
MUST NOT have exactly one action it cannot undo.

### R5 — `FrontmatterOnly` stays in the policy model (Feature-Scoped Invariant)

The `WriteMode.FrontmatterOnly` enum value and its `"frontmatter-only"` parser case are
retained even though no shipped policy declares it. Removing the recognized value would turn
an operator's existing policy file into a fail-closed load error on upgrade — a worse outcome
than an unused vocabulary word. This ADR records that it is unused by design, so a future
reader does not mistake its absence from policies for a bug.

### Consequences

- Good: the remediation loop completes; an authorized body edit applies.
- Good: one scope, one artifact, one hash — nothing to keep in sync, and the smallest
  possible diff at the coordinators (none: they already pass the same path).
- Good: deletion cannot leak to Ingest or Query.
- Bad: an unattended survey run can now rewrite or delete any page. The compensating control
  is git history, which is a human recovery path, not an automated one. This is a deliberate
  and explicitly accepted trade.
- Bad: ADR-016's structural guarantee disappears from the deterministic tier. What replaces
  it is agent-judgment evaluation coverage (spec SC-013), which is sampled, not absolute.
- Bad: concurrent Lint and Ingest writes to `index.md`/`log.md` are now possible, making
  ADR-015's cross-process coordination load-bearing for two more files.

## Rule Classification (Principle III)

| Rule | Category | Enforcement |
|---|---|---|
| R1 one scope, no mode branch | Boundary Rule | Phase 0 structural test + behavioral test |
| R2 reserved files in scope | Boundary Rule | Behavioral test (policy-level) |
| R3 deletion is a separate deny-by-default scope | Boundary Rule | Phase 0 structural test + Red/Green probe |
| R4 deletions are journaled and rolled back | Boundary Rule | Classicist integration test |
| R5 `FrontmatterOnly` retained but unused | Feature-Scoped Invariant | Classicist test: a policy declaring it still loads |
