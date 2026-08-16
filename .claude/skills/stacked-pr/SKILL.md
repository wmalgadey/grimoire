---
name: stacked-pr
description: Slice a Spec Kit feature into a stack of small, sequentially reviewable pull requests with gh-stack instead of one big-bang PR. Use when starting implementation of a feature whose tasks.md has several phases, or when the user asks to stack PRs, split a PR, or avoid a big-bang PR.
allowed-tools: Bash, Read, Edit, Grep, Glob
---

# stacked-pr — Ship a Feature as a Stack, Not a Big Bang

A Spec Kit feature lands today as one pull request per feature branch. For a large
feature that means an unreviewable diff: `023-task-ui-improvements` merged as 84 files
and +5856 lines in a single PR. This skill structures the same work as a **stack** —
an ordered chain of small pull requests, each targeting the branch below it, all
landing on `main`.

Nothing about the Spec Kit workflow changes. The feature still has one `spec.md`, one
`plan.md`, one `tasks.md`, and one Definition of Done. Only the *delivery shape*
changes: several reviewable PRs instead of one.

## Tooling

Stacks are managed with [`github/gh-stack`](https://github.com/github/gh-stack), the
official GitHub CLI extension for stacked pull requests:

```bash
gh extension install github/gh-stack   # requires gh >= 2.0
```

Stack metadata lives in `.git/gh-stack` — local only, never committed. All branches
must be in the same repository (cross-fork stacks are unsupported), which is how this
repo already works.

**Availability check first.** Run `gh stack --help` before relying on any stack
command. If `gh` or the extension is missing — notably in remote/web agent sessions,
which have no `gh` CLI — do not fail and do not fall back to a big-bang PR. Build the
same layer branches by hand and open each PR with `base` set to the branch below it;
the result is an equivalent stack that `gh stack link` can adopt later from a machine
that has the extension.

## Cutting the layers

`tasks.md` is already sliced along the right lines — its phases are the layer
boundaries. Map them in this order, bottom (closest to `main`) to top:

1. **Phase 0 — structural boundary tests.** Always the bottom layer. This matches the
   constitution's non-negotiable ordering: the Red/Green-probed boundary test is
   verified before any feature code exists. If the feature introduces no Boundary Rule,
   fold Phase 0 into the setup layer.
2. **Setup and Foundational phases.** The blocking prerequisites every story builds on.
3. **One layer per user story**, in the priority order `tasks.md` already gives (P1
   before P2 before P3). This is the point of the exercise: each story is independently
   reviewable, and the MVP story can merge while later stories are still in review.
4. **Polish, cross-cutting concerns, and convergence.** The top layer.

**Aim for 3–6 layers, not one per phase.** A ten-layer stack costs more review
attention than it saves — group the small phases (setup + foundational together;
adjacent P3 stories together). Fewer, coherent layers beat a faithful 1:1 mapping of
the phase list.

Every layer must be independently sane: it builds, its own tests pass, and it does not
depend on code that only appears in a layer above it.

## Branch naming

Layer branches keep the feature's numeric prefix so `/speckit-git-validate` and the
`specs/NNN-*` lookup keep working:

```
023-task-ui-improvements-01-phase0
023-task-ui-improvements-02-foundational
023-task-ui-improvements-03-us1-status-history
```

Feature resolution is not affected by the branch name — `.specify/scripts/bash/common.sh`
resolves `FEATURE_DIR` from `SPECIFY_FEATURE_DIRECTORY` or `.specify/feature.json`, never
from the current branch. Every `/speckit-*` command keeps resolving the same
`specs/NNN-feature-name/` directory no matter which layer is checked out.

## Working the stack

```bash
gh stack init -b main                     # from the feature branch, once
gh stack add -m "<commit message>"        # start the next layer (from the top branch)
gh stack submit                           # push branches, create/update the PRs
gh stack sync                             # fetch, rebase the whole stack, push
gh stack view                             # see the stack and its PR links
```

Rules that keep a stack from going sideways:

- **Commit before `gh stack add`.** The `after_implement` auto-commit hook commits to
  whatever branch is checked out. Uncommitted work at the moment you add a layer lands
  in the wrong one.
- **Touch `tasks.md` in one layer only.** Every phase ticks checkboxes in the same
  file, and a bottom-layer merge rebases the whole stack — per-layer checkbox edits are
  the single largest source of cascading rebase conflicts. Update the task list in the
  top layer, or in one dedicated commit at the end.
- **Merge from the bottom up.** When the bottom PR merges, the remaining branches are
  rebased automatically so the next PR retargets `main`. Run `gh stack sync` afterwards.
- **Never rebase a layer by hand** while the stack is live; use `gh stack rebase` /
  `gh stack sync` so the tracked stack state stays consistent.

## What stays whole-feature

Splitting delivery does not split the Definition of Done. These are properties of the
*feature*, not of any single layer, and are satisfied at the top of the stack before the
last PR merges:

- the `plan.md ## Observability` completeness audit,
- agent-behavior evaluation tests for every agent-judgment success criterion,
- `/speckit-converge`.

Per-layer, the ordinary merge gates still apply to every PR in the stack — branch
protection and CI run on mid-stack PRs exactly as on the bottom one. Two consequences
worth stating to the user up front:

- CI cost multiplies by the number of layers (the full suite, SlowEval evals included,
  runs per layer).
- The complexity gate improves: `complexity.yml` measures against
  `pull_request.base.sha`, so each layer is measured against the layer below it rather
  than against `main` — a true per-layer delta.

## Non-goals

This is a delivery convention, not an architectural boundary. It needs no ADR: ADRs
record constraints on the Grimoire system, and how a contributor shapes a pull request
is not one. It also adds no hooks to `.specify/extensions.yml` — stacking is a judgment
call per feature, not an automatic step in the Spec Kit sequence.
