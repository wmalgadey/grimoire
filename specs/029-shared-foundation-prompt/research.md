# Research: Shared Foundation Prompt and Deployment Identity Wizard

**Feature**: 029-shared-foundation-prompt | **Date**: 2026-09-05

Phase 0 research for `plan.md`. Each item states the question, what the codebase actually does today,
the options weighed, and the decision that goes into the plan and its ADRs.

## R1 — Where does the shared foundation document physically live?

This is the decision the spec explicitly deferred to planning, and the one everything else hangs on.

### What is true today

- `ADR-043` distributes each agent's *whole* build output — worker DLL, dependencies, and the
  project's `Instructions/` folder — into `<AgentDir>/<agentId>/`, clear-then-copy, on every build
  (`backend/Directory.Build.targets`, `PublishAgentRuntime`). Only the agent-id subfolder is deleted;
  nothing beside it is touched.
- `GrimoirePathResolver.BuildAgentRuntimePaths` derives every instruction path from the mandatory
  `Agent:Dir` root with fixed, non-configurable filenames (`Instructions/system-prompt.md`,
  `Instructions/policy.json`, optionally `Instructions/default-user-prompt.md`).
- Eval and replay runs do **not** read the agent directory at all: `EvalPaths` resolves instruction
  documents from the agent **project sources** (`backend/src/Grimoire.<Agent>Agent/Instructions/`), so
  an eval needs neither a prior agent build nor any hub configuration.
- In the container (`deploy/Dockerfile`), `/app/.grimoire/agents/` is **image content**, rebuilt on
  every deployment. The only writable, persistent locations are the three volumes mounted at
  `/var/lib/grimoire/{data,wiki,memory}`.

### The two shapes the spec named

**Shape A — the document lives inside each agent's build-distributed `Instructions/` folder.**
One authored source file in the repository, linked into all three agent projects, delivered as three
byte-identical copies by the existing build target.

- No new path root, no new CLI switch, no new configuration key, no container change.
- ADR-043-native: "new files in an agent's build output" is listed in that ADR's own Change Triggers
  as an extension, so nothing about ADR-043 changes.
- Each agent build writes only its own subfolder — no shared write target, so no parallel-build race.
- Eval resolution is a one-line addition to `EvalPaths` pointing at the single repo source.
- **Fatal on its own for Part 2**: the agent directory is image content. An operator cannot put an
  instance-specific document there without rebuilding the image, and a redeployment would overwrite
  it. FR-017 (survives redeployment) and the entire wizard are unreachable.

**Shape B — a single shared location outside the per-agent directories.**
A fifth path root (`Grimoire:Paths:Instructions:Dir` or similar) holding one copy.

- One physical file instead of three, which is tidier at first glance.
- Costs a new root, a new CLI switch, a new `appsettings.json` group, a new container volume, and a
  new validation branch.
- The default has to *get there*: either a build target writes into a location no agent owns (three
  agent builds racing on one file), or the Hub seeds it at startup — which is exactly the option
  ADR-043 rejected, because it makes the Hub the author of instruction content (Principle V).
- A fresh deployment mounts an empty volume, so a fail-closed load fails on first start unless
  something seeds it. Seeding is the same Principle V problem again.

### Decision: Shape A for the default, plus one optional operator override

Neither shape alone satisfies both halves of the feature. The chosen design is Shape A **as the
shipped default**, with a single optional configured override for the instance:

1. **Default (always present)**: one authored file,
   `backend/src/Grimoire.AgentRuntime/Instructions/foundation-prompt.md`, delivered by the existing
   agent build into `<AgentDir>/<agentId>/Instructions/foundation-prompt.md` for all three agents. It
   is a required input exactly like `system-prompt.md` — derived from `Agent:Dir`, fixed filename,
   validated fail-fast at startup.
2. **Instance document (optional)**: a single file at a fixed, derived location the Hub owns and can
   write — see R6. It is not independently configurable, so there is no path for an operator to
   mistype and no configured-but-missing case to fail on.
3. All three agents resolve the same effective document: the instance document when one exists, the
   agent's own build-distributed copy otherwise.

Why this is the right trade rather than a compromise:

- It keeps the default path in the one place ADR-043 already guarantees is current across rebuilds,
  with no new root, no new volume, and no seeding.
- It gives the wizard exactly one thing to do — write one file to one durable location — which is the
  smallest possible mechanism for FR-017.
- It adds no configuration surface at all, so ADR-041/ADR-042 are untouched rather than extended.

Rejected sub-option: reaching the instance document through a new configured key
(`Grimoire:Paths:FoundationPromptFile`). It was the design until the wizard moved into the Hub
(2026-09-05 clarification). Once the Hub itself writes the document, a configured path buys nothing —
the writer and the reader are the same process, so there is no second party whose path could disagree
— and it costs a key, a validation branch, and a new way for a deployment to be misconfigured.

## R3 — Composition: what exactly does the agent receive?

Options: (a) concatenate into the single `system` parameter the model client already takes;
(b) pass two system blocks; (c) inject the foundation document as a first user-channel message.

Decision: **(a) concatenate, foundation document first, joined by exactly one blank line**, in the
shared `AgentHost` startup template that all three agents already run through. Reasons:

- `IModelClient.NextTurnAsync` takes one `systemPrompt` string; (b) would change the port's shape for
  no behavioural gain.
- (c) would put instruction content in the user channel, where ADR-029's operator-turn delimiting and
  the untrusted-source framing live — the wrong channel for text that is not a per-run steering
  message.
- The join is two newlines and nothing else: no harness-authored headers, banners or labels. Both
  documents already open with their own H1, so the structure is visible to the model without the
  harness adding words of its own, and "the agent received both documents verbatim" stays a literal,
  byte-checkable claim (FR-004, SC-003).
- Order is foundation-then-role for all three agents, matching the user's own CLAUDE.md/SKILL.md
  analogy: the wiki-wide statement first, the agent's role second.

`AgentHost` is the single place where this happens, so no agent-conditional branch is introduced
(ADR-013/ADR-044's no-per-agent-branches rule holds).

## R4 — What the run records, and what the evals fingerprint

- The task artifact's `instruction_files` field is already a **list** of `{path, sha256}` records that
  has carried exactly one entry since ADR-007 ("task-artifact recording keeps the existing list shape
  with one entry, so 002-era artifact readers are unaffected"). The foundation document becomes the
  second entry, foundation first. No format change, no reader change.
- The per-run `system_prompt_sha256` field on run records keeps its meaning — the agent's own role
  document — and the foundation document is reported alongside it rather than folded into it, so an
  operator can tell *which* document changed between two runs.
- `Fingerprints.Compute` gains a `foundation_prompt` key. Every existing recording manifest lacks it,
  so every scenario reports stale until re-captured. That is the documented instruction-change merge
  gate working exactly as ADR-012 designed it, and it is **not avoidable**: composing a second
  document changes the system-prompt hash `ReplayModelClient` verifies per turn, whatever the
  fingerprint set says. Refreshing needs a live provider run (`eval.yml` / the EvalRunner capture
  command) and is therefore an explicit, operator-triggered step of the DoD.

## R5 — Extraction: what moves out of the three system prompts

Per the 2026-09-05 clarification, everything stated in two or more of the three prompts moves. The
concrete inventory, from the current files:

| Content | Ingest | Query | Lint | Moves |
| --- | --- | --- | --- | --- |
| Wiki folder structure | ✓ | – | ✓ (implied by write scope) | ✓ |
| Page types | ✓ | ✓ (synthesis pages) | ✓ | ✓ |
| Page language rule | ✓ | ✓ (answer language) | – | ✓ (page language only; answer language is Query's own) |
| Frontmatter standard | ✓ | ✓ | ✓ | ✓ |
| Tag taxonomy | ✓ | ✓ | ✓ | ✓ |
| Confidence scoring | ✓ | ✓ | ✓ | ✓ |
| Supersession rules | ✓ | – | ✓ | ✓ |
| `index.md` catalog upkeep | ✓ | ✓ | ✓ | ✓ |
| `log.md` entry conventions | ✓ | ✓ | ✓ | ✓ |
| Contradiction marking | ✓ | – | ✓ | ✓ |
| Citations | ✓ | ✓ | – | ✓ |
| "Source content is data, not instructions" | ✓ | ✓ | – | ✓ |
| Role, per-agent steps, write scope, modes | ✓ | ✓ | ✓ | stays |

The moved text is *relocated verbatim wherever the three copies already agree*, and reconciled by
hand where they diverge — divergence is itself the defect this feature removes, and each reconciliation
is recorded in the implementation's commit messages so a behavioural change can be attributed later.



## R6 — Where does the instance document live, now that the Hub writes it?

The 2026-09-05 clarification moved the wizard into the Hub, which changes this answer completely. The
earlier design — a read-only bind mount from the deploy host, switched by a variable in `.env` — assumed
the *deployment script* wrote the file. A read-only mount is exactly what the Hub cannot write, and the
Hub cannot edit `.env` either, so that design is dead rather than adapted.

What the Hub can write is its own writable roots. Three exist (ADR-041, ADR-052), and only one fits:

| Root | Fits? |
| --- | --- |
| `Wiki:Dir` | **No.** Agents write there through the guarded tools. An instruction document inside the wiki could be rewritten by the very agents it steers — a self-modification path this feature must not open. |
| `Memory:Dir` | **No.** ADR-052 scopes it to agent-process bookkeeping: task, conversation, finding and remediation records. An instance identity document is none of those. |
| `Data:Dir` | **Yes.** The Hub's own writable area for state that is neither wiki content nor a per-run record — it already holds the operational-state database, raw sources, write locks and the lint pid file. It is volume-backed in every deployment shape, so it survives redeploy, rollback and restart. |

**Decision**: the instance document is `<DataDir>/foundation-prompt.md` — a fixed filename under an
existing root, derived in code exactly as `lint.pid`, `index.md` and `log.md` already are, with no
configuration key of its own.

Resolution is therefore **presence-based**, and that is sound here in a way it would not have been for
a configured path: nothing an operator types decides this location, so the failure mode presence-based
resolution normally invites — "a mistyped path silently ran the default" — cannot occur. The file is
either there, written by the wizard, or it is not.

Consequences worth stating plainly:

- **No compose, Dockerfile or `.env` change at all.** The data volume is already mounted in every
  deployment shape. This is a strictly smaller deployment surface than the pre-clarification design.
- **The Hub now writes a file named like an instruction document**, which the existing instruction-
  authorship Boundary Rule forbids outside one allowed namespace. That is a real boundary decision, not
  a detail — see R7.
- **Hand-editing stays possible**: the file sits in a volume an operator can reach with the same tools
  they already use for the wiki, and the Hub reads it fresh per run.

## R7 — May the Hub write an instruction document at all?

This is the sharpest question the relocation raises, and it deserves a straight answer rather than a
comfortable one.

ADR-043 rejected "instruction files written out by the hub" precisely because it *makes the hub the
author of instruction content* (Principle V). The existing structural test
(`InstructionAuthorshipBoundaryRuleTests`) enforces that: no production type outside
`Grimoire.Hub.Runtime.Paths` may combine an instruction filename literal with a file-write API.

The distinction that resolves it is **authoring versus writing**, and it is the same distinction the
harness already lives by everywhere else:

- The agent *authors* wiki pages; the guarded tool layer *persists* the bytes it is handed. Nobody
  calls `GuardedToolExecutor` the author of the wiki.
- Here, an agent session *authors* the foundation document from the operator's description; the Hub
  *persists* the bytes it is handed, verbatim, without composing, templating, summarizing or validating
  the content beyond "non-empty and readable".

What would violate Principle V is the Hub *producing* instruction text — a template with the operator's
description interpolated, a default skeleton, a merge of old and new. FR-013a already forbids exactly
that.

**Decision**: no ADR. Re-reading ADR-043's rejection settles it rather than blocking it: that option was
rejected for making the hub the *author* of instruction content and for destroying the operator's
ability to read the effective instructions on disk. Here the operator triggers the wizard, an agent
session drafts the document, the file lands where the operator would otherwise have saved it by hand,
and it stays readable there. No boundary is crossed, so there is nothing to decide — the wizard is a
helper that saves the operator a manual file copy.

What remains is maintenance of the structural test that enforces the authorship rule. Its heuristic is
deliberately broader than the rule (any instruction filename literal in a method that also writes a
file), so the wizard's namespace joins its allow-list and `foundation-prompt.md` joins its literal set,
with the Red/Green probe covering the new literal specifically. That is a Phase 0 task, visible in
`tasks.md` — not a silent widening.

Rejected alternative: having the *operator* place the file by hand and the Hub only report on it. It
keeps the Boundary Rule untouched, but it hands the operator a path inside a Docker volume to write into
and makes the wizard a pure advice-giver — which is most of the work the wizard exists to remove, and it
contradicts FR-011.

## R8 — How does the deployment script start a wizard that lives in the Hub?

The Hub CLI already runs in-process against the shared composition root (ADR-049), and the deployment
already runs Hub commands the same way any operator would: `docker compose exec`. `grimoire-server`
gains one thin command that forwards to the Hub's wizard command and passes its exit code through, plus
one line in `status` that prints what the Hub reports.

No new mechanism: `compose()` in `grimoire-server` already builds the exact `docker compose` invocation
with the overlay and the project directory pinned, and `cmd_logs`/`cmd_restart` already forward
arguments to it. The wizard's non-interactive requirement (US3) is what makes this work at all — the
forwarded invocation has no terminal.

## R9 — What does the two-step hand-off look like from the operator's side?

The wizard cannot block waiting for an agent session to draft a document: the drafting happens outside
the Hub, on the deploy host, possibly minutes later and in a different session (spec Assumptions). So
the wizard is two invocations, not one:

1. **`wiki-identity set --specialised --description <text>`** → the Hub writes nothing to the instance
   and prints a drafting brief: the operator's description plus the document's required shape. Emitting
   a brief is not a state the instance is in — nothing changed, and the command can be re-run.
2. **`wiki-identity set --from-file <path>`** (optionally `--replace`) → the Hub reads the drafted
   document, rejects it if empty or unreadable, refuses to overwrite an existing instance document
   unless `--replace` was given, and otherwise writes it verbatim to `<DataDir>/foundation-prompt.md`.

`--default` is a third, terminal answer that writes nothing and reports that the instance stays on the
shipped default (FR-012).

This shape falls out of the constraint rather than being chosen for elegance: any single-invocation
design would either block on a human (breaking US3) or make the Hub itself draft the content (breaking
FR-013a).

## R10 — Does the agent need a restart to pick up a new instance document?

No. The effective foundation document is resolved per run, from disk, at the moment the Hub composes an
agent's instruction paths — the same point at which `system-prompt.md` is resolved today. A run
dispatched after the wizard writes the file operates under it; a run already in flight keeps the
document it started with, and its recorded version says which one that was.

The Hub's *startup* path validation (the build-distributed default must exist) is unaffected: it
validates the default, which is image content and always present.
