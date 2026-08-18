---
name: issue-triage
description: Triage the open GitHub issues — label new arrivals, name blockers, resolve released blocks, and update the pinned triage map issue. Use after a feature completes (before choosing the next one), on a scheduled triage run, or when the user asks to triage, re-triage, or clean up the issue board.
allowed-tools: Bash, Read, Grep, Glob
---

# issue-triage — Keep the Issue Board Decision-Ready

The open backlog is only useful when three questions are answerable from the issue
list itself, without opening anything: *what can I pick up right now?*, *what is
waiting, and on what?*, and *which decision unblocks the most work?* This skill is
the recurring procedure that keeps those answers current. The durable state lives
in the **triage map issue**: find it as the pinned open issue whose title starts
with "Triage map:" — the pin and title prefix are the source of truth, not any
hard-coded number (at the time of writing it is #133). Each run updates it.

Triage organizes and surfaces. It does **not** make the decisions themselves —
`decision-needed` issues are resolved by the maintainer, not by a triage run.

## Label taxonomy

Three triage labels, alongside the existing kind labels (`bug`, `enhancement`,
`housekeeping`, `spec-candidate`, `question`, `adr`, `tail-tasks`):

| Label | Meaning | Criterion |
| --- | --- | --- |
| `quick-fix` | No spec, no decision needed | An S/M change whose fix surface is known; SDD workflow not required (bug fix, config knob, test hygiene) |
| `decision-needed` | A written decision unblocks work | The issue's next step is a choice (ADR, contract semantics, scope split) — not code |
| `blocked` | Waits on another open issue | A named issue must land or be decided first; every `blocked` issue carries a "Blocked by" comment naming its blockers and why |

Rules:

- A `blocked` issue names its blockers in a **comment**, not by editing the issue
  body (the MCP read path HTML-escapes bodies; a body round-trip corrupts code
  blocks). Comments also create timeline backlinks on the blocking issue.
- Real containment (issue X is a part/shape-decision of issue Y) is a native
  **sub-issue** relation, not a label.
- When editing labels, always carry the issue's existing labels along unless one
  is being removed on purpose: label-update APIs commonly **replace** the whole
  set rather than append (the GitHub MCP `issue_write` tool does). Verify the
  behavior of whichever tool is in use before the first label write of a run.

## Per-run procedure (delta, not full re-read)

1. **Establish the delta.** List open issues updated since the last triage run
   (the map issue's last-edited date is the watermark; use the `since` filter).
   Also list issues **closed** since then.
2. **Triage new/changed issues.** For each: assign it to a cluster in the map,
   apply the taxonomy label that fits, assign the best-fitting milestone (see
   "Milestones" below), and if `blocked`, post the "Blocked by" comment. If it
   duplicates or is contained by an existing issue, propose the fold (sub-issue
   link or close-as-duplicate) — execute it if the containment is explicit in
   the issue's own text, otherwise flag it in the run summary.
3. **Release resolved blocks.** For each closed issue, find open issues whose
   "Blocked by" comment names it. If all named blockers are now closed/decided,
   remove the `blocked` label and comment that the issue is unblocked.
4. **Age the decision gates.** Any `decision-needed` issue untouched for more
   than ~30 days gets listed in the run summary as stale — the point is to keep
   pending decisions visible, not to nag on every run.
5. **Update the map issue.** Check off landed items, add new issues to their
   cluster/wave lists, and update the mermaid dependency graph if edges changed.
6. **Summarize.** End with a short report: newly triaged (with labels and
   milestones given), blocks released, stale decisions, and any proposals
   awaiting the maintainer (folds, milestone escalations, issues no milestone
   fits). If the delta was empty, say so and change nothing.

## Milestones

Every open issue carries a milestone; the repository's milestone list is the
source of truth for which ones exist and what each release means (as of this
writing: a version milestone per release plus a dateless "Tech debt & evals"
bucket for opportunistic refactors and eval methodology). Rules:

- **Assign by theme, not urgency.** A new issue goes to the milestone whose
  theme it belongs to — the furthest-out bucket it fits, by default.
- **Escalating into the release currently in progress** (the earliest open
  version milestone) is a scope decision, not a filing default: a triage run
  proposes it in the run summary instead of assigning it silently. Only an
  issue that describes the current release's own functionality being broken
  goes straight in.
- **Never create milestones** during triage. If no existing milestone fits,
  leave the issue unassigned and flag it in the run summary — new buckets are
  the maintainer's call.
- The triage map issue itself stays milestone-less; it is a living document,
  not deliverable work.

## Hygiene at the source

A session that **files** issues applies this taxonomy at creation time: kind
label plus, where already clear, `quick-fix` / `decision-needed` / `blocked`
(with the "Blocked by" comment) — and a milestone per the rules above. A
well-filed issue makes the next triage run a no-op.

## When a feature completes

The natural triage moment is after `/speckit-converge` / the feature's final PR
merge, before the next feature is chosen: run this skill first, then pick the
next unit of work from the map's wave lists — `quick-fix` batches between
features, the top unblocked spec-candidate as the next SDD feature.
