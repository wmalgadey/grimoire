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
2. **Instance override (optional)**: one new configuration key, `Grimoire:Paths:FoundationPromptFile`,
   shipped in `appsettings.json` with an empty value. Empty means "no instance override — use the
   build-distributed default". A non-empty value is a **required input**: it must resolve to an
   existing file or startup fails, naming the key. There is no presence-sniffing and no silent
   fallback for a configured-but-missing path, so a typo is loud rather than invisible.
3. All three agents resolve the same effective document: the override when configured, the agent's own
   build-distributed copy otherwise.

Why this is the right trade rather than a compromise:

- It keeps the default path in the one place ADR-043 already guarantees is current across rebuilds,
  with no new root, no new volume, and no seeding.
- It gives the wizard exactly one thing to do — put a file somewhere durable and point the deployment
  at it — which is the smallest possible mechanism for FR-017.
- The override is the same shape as `SecretsFile`, an operator-supplied file input that already lives
  outside the four roots and is validated as a required input. That is a pattern the codebase, the
  compose file and `deploy/README.md` already explain.
- Extension, not invalidation, for ADR-042: its Change Triggers name "new configuration keys or groups
  added to the file for new options, roots, or sub-paths" as an extension, and the new key's default
  value lives in `appsettings.json`, not in code.

Rejected sub-option: making the *override* presence-based (a fixed path that is used when the file
happens to exist). It removes one configuration key at the cost of turning a mistyped mount into
"silently ran with the default" — the exact silent-fallback failure mode ADR-042 exists to prevent.

## R2 — How does an instance-specific document reach a containerized deployment?

`compose.yaml` gains one read-only bind mount and one environment entry:

```yaml
Grimoire__Paths__FoundationPromptFile: /app/foundation-prompt.md
volumes:
  - ${GRIMOIRE_FOUNDATION_PROMPT:-./backend/src/Grimoire.AgentRuntime/Instructions/foundation-prompt.md}:/app/foundation-prompt.md:ro
```

- Unset (`GRIMOIRE_FOUNDATION_PROMPT`): the mount source is the repository's own default document, so
  the container runs the shipped default — byte-identical content to the copies inside the image.
  This satisfies FR-012 without the wizard having to edit any YAML.
- Set: it points at the operator's file on the host, mounted read-only. `.env` is already the
  documented place for this class of variable (`GRIMOIRE_WIKI_DIR`, `GRIMOIRE_UID` work the same way),
  and compose reads it from the project directory, which `grimoire-server` pins to the checkout.
- The bind source always exists, so Docker never materializes an empty directory in its place.

The wizard therefore writes two things and edits no YAML: the document itself, and one variable in
`.env`. Both live outside the checkout's tracked content, so `deploy`, `rollback` and `restart` leave
them alone (FR-017).

Where the document goes on the host: `$(state_dir)/foundation-prompt.md` — the directory
`grimoire-server` already owns for its deployment record, tool record and compose overlay. It is
outside the checkout, so no git operation can touch it, and `status` can read it directly to answer
FR-018.

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

## R6 — Does the wizard need the Hub, Docker, or a running stack?

No. It writes a file and a variable; the deployment picks them up on its next `up`/`restart`.
`grimoire-server` already re-creates containers on `deploy` and `restart`, so the documented sequence
is "run the wizard, then `grimoire-server restart` (or `deploy`)". The wizard reports that explicitly
rather than restarting the stack behind the operator's back — a wizard that silently bounces a running
deployment is a worse surprise than one extra command.

## R7 — Non-interactive form

`cmd_tmux` already establishes the house pattern (`[[ ! -t 0 || ! -t 1 ]]` → do the useful thing and
say how to reach it, instead of failing at the terminal layer). The wizard follows it:

- `--default` / `--specialised` supply the first answer.
- `--from-file <path>` supplies the drafted document.
- `--replace` is the explicit decision required to overwrite an existing document.
- Any answer that is still missing when there is no TTY is an immediate non-zero exit naming the flag
  to pass. With a TTY, the same missing answer is a prompt.
