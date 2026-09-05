# Feature Specification: Shared Foundation Prompt and Deployment Identity Wizard

**Feature Branch**: `claude/shared-foundation-prompt-j3jdwi`

**Created**: 2026-09-05

**Status**: Draft

**Input**: User description: "Give every agent one shared foundation prompt, and a deployment wizard that sets it for this instance.

This delivers the part of issue #217 that is wanted, and explicitly declines the rest. #217 asked for a generator that produces three per-agent `system-prompt.md` files from a wiki-purpose description. That generator is NOT being built — not at runtime, not at build time, and not as a one-shot setup tool that rewrites the three agent prompts. The three per-agent system prompts stay hand-authored, version-controlled product content. What is being built instead is a fourth instruction file that all three agents load in addition to their own, plus an operator wizard in `deploy/server/grimoire-server` that decides what that fourth file says for a given deployment. Part of closing this feature is recording the declined scope on issue #217 itself, so the board stops carrying the generator as pending work.

## Part 1 — a shared foundation prompt loaded by every agent

This will close issue #137

Today each agent (ingest, query, lint) loads exactly one `system-prompt.md` (ADR-007), delivered inside its own agent directory by the agent build (ADR-043, `<AgentDir>/<agentId>/Instructions/`), resolved by `GrimoirePathResolver`, passed to the child process as `--system-prompt-path` by `AgentProcessHost`, loaded verbatim, fail-closed, with its SHA-256 recorded in the task artifact. Everything the three agents share about *what this wiki is and how it works* is therefore duplicated three times, in three separately maintained files, with nothing keeping them consistent.

Introduce a single shared instruction document — the wiki's foundation: what kind of wiki this instance maintains, what the wiki is for, and the conventions that hold across every agent's work. Each agent loads it **in addition to** its own `system-prompt.md`, so all three start from the identical statement of the wiki's purpose, and each agent's own file is left to say only what is specific to that agent's role.

The idea is, that the main instruction could be seen as a CLAUDE.md and the agent specific instrructions could be seen as SKILL.md for the specific agent.

- The shipped default content is the wiki Grimoire maintains today: a general, personal knowledge LLM-wiki.
- Composition is harness work, and it is mechanical: the harness concatenates or otherwise composes the shared document with the agent's own system prompt in a fixed, documented order and hands the result to the agent.
- All three agents need it. The eval runner and any replay path must resolve it the same way a real run does, without extra operator configuration (ADR-043's driver).
- Where the shared document physically lives is an open decision the plan must make: ADR-043 places instruction documents inside each agent's own build-distributed directory, which would mean three build-time copies of one logical document, while a single shared location outside the per-agent directories is a new path root with its own resolution, CLI switch and container-image consequences. Both options have to be weighed; neither should be assumed.

## Part 2 — a deployment wizard in `grimoire-server`

This will close issue #217

Add a command to `deploy/server/grimoire-server` that walks an operator through the wiki-identity decision as part of bringing up a deployment, and puts the resulting shared foundation prompt in place for that instance or leaves the defaults.

- It asks one question first: keep the default knowledge LLM-wiki, or maintain a specialised wiki. Answering "default" is a complete, valid outcome and must leave the instance byte-identical to a deployment that never ran the wizard.
- Answering "specialised" collects the operator's own description of the wiki they want maintained and produces the instance's shared foundation prompt from it. The wizard could be a spezfic prompt for claude code, and claude code could run the actual wizard and create the needed files. the wizard could the just ensure, that the files are placed, where they belong
- The wizard must be safe to re-run. Re-running it against an instance whose identity was already set, or hand-edited afterwards, must never silently discard the existing document — it reports what is there and requires an explicit decision before replacing it.
- It must work without a TTY. `grimoire-server` is routinely invoked from a non-interactive Claude Code session on the deploy host (see how `cmd_tmux` handles `! -t 0`), so the command needs a non-interactive form — flags that supply the same answers — and must fail with a clear message rather than hang when it is asked to prompt with no terminal.
- `grimoire-server status` should report which wiki identity the running deployment is steered by, in the same way it reports the deployed ref and the tool version. An operator asking "what is running on this host?" gets an incomplete answer today if two instances of the same commit maintain different wikis.
- the `deploy/server/grimoire-server`-cli is no real implementation detail or better said, no system component. it is just an helper and can be build out of scope, we do not need the same testing and workflow requirements like the front- or backend."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One document states what this wiki is, and every agent starts from it (Priority: P1)

A maintainer wants to change something that is true of the whole wiki — what kind of knowledge it
collects, what the wiki is for, a convention that holds across ingesting, answering and linting. Today
that means finding the same statement in three separately maintained agent instruction files and
editing all three consistently. After this feature there is exactly one document that says it, every
agent run loads that document in addition to its own role instructions, and the three per-agent files
carry only what is specific to that agent's role.

**Why this priority**: It is the whole point of the feature and the only part that changes agent
behavior. Part 2 has nothing to set without it. It also stands alone: shipped with its default content
and no wizard at all, the system behaves exactly as it does today while removing the triplication.

**Independent Test**: Edit the single shared document to state something the per-agent files do not
say, dispatch one run of each agent type, and confirm from each run's own record that the loaded
instruction set contained that statement — and that the per-agent files were not edited to achieve it.

**Acceptance Scenarios**:

1. **Given** a deployment with the shipped default foundation document, **When** an ingest, a query and
   a lint run are dispatched, **Then** each run's task record lists both the shared foundation document
   and that agent's own system prompt, each with its own content hash.
2. **Given** the shared foundation document and an agent's own system prompt, **When** any agent run
   starts, **Then** the instruction text the agent receives contains both documents' content verbatim,
   in the same documented order for every agent type.
3. **Given** a shared foundation document that is missing, unreadable or effectively empty, **When** a
   run is dispatched, **Then** the run fails before any wiki write, naming the document that could not
   be loaded.
4. **Given** an evaluation or replay run started without any operator-specific configuration, **When**
   it resolves the agent's instructions, **Then** it resolves and composes the shared foundation
   document exactly as a real dispatched run does.

---

### User Story 2 - An operator decides what wiki this instance maintains, while deploying it (Priority: P2)

An operator bringing up a deployment is asked one question: keep the default general-knowledge wiki, or
maintain a specialised wiki. Keeping the default is a complete answer and leaves the instance
indistinguishable from one that was never asked. Choosing "specialised" lets the operator describe, in
their own words, the wiki they want maintained, and the deployment ends up steered by a foundation
document that says so — without the operator hand-editing any product file.

**Why this priority**: It is what makes the shared document per-instance rather than merely
deduplicated, and it is the operator-facing half of the feature. It depends on US1 being in place.

**Independent Test**: On a fresh deployment, run the wizard choosing "default" and confirm the instance
is byte-identical to one that never ran it; then run it choosing "specialised" with a description, and
confirm the running deployment's agents are steered by a foundation document reflecting that
description.

**Acceptance Scenarios**:

1. **Given** a deployment that has never run the wizard, **When** the operator runs it and chooses the
   default wiki identity, **Then** no instruction content is added, changed or removed anywhere in the
   instance, and subsequent runs load the shipped default foundation document.
2. **Given** a deployment that has never run the wizard, **When** the operator chooses a specialised
   wiki and supplies a description, **Then** the instance's foundation document reflects that
   description and every subsequent agent run of every type loads it in place of the shipped default.
3. **Given** an instance whose foundation document was already set (by an earlier wizard run or by
   hand), **When** the wizard runs again, **Then** it reports what is already in place and does not
   replace it until the operator explicitly decides to.
4. **Given** an instance-specific foundation document in place, **When** the deployment is redeployed to
   another ref or rolled back, **Then** the document survives and still steers the agents.

---

### User Story 3 - The wizard works from a session that has no terminal (Priority: P2)

`grimoire-server` is routinely driven from a non-interactive Claude Code session on the deploy host.
The wizard must therefore be fully usable without a terminal: every answer it would otherwise prompt for
can be supplied up front, and when it is asked to prompt with no terminal attached it says so and exits
instead of hanging.

**Why this priority**: Without it the wizard is unusable in the way this deployment is actually
operated. It is a distinct, independently testable behavior of the same command as US2.

**Independent Test**: Run the wizard with stdin redirected from `/dev/null`, once with every answer
supplied as flags (succeeds) and once with an answer missing (fails immediately with a message naming
the missing answer, rather than blocking).

**Acceptance Scenarios**:

1. **Given** no terminal on stdin, **When** the wizard is invoked with all answers supplied as flags,
   **Then** it completes without prompting and produces the same outcome as the interactive run with the
   same answers.
2. **Given** no terminal on stdin, **When** the wizard is invoked without an answer it needs, **Then** it
   exits non-zero within seconds with a message naming what to supply, and changes nothing.
3. **Given** an existing foundation document and a non-interactive invocation that does not carry an
   explicit decision to replace it, **When** the wizard runs, **Then** it exits non-zero without touching
   the existing document.

---

### User Story 4 - "What is running on this host?" includes which wiki it maintains (Priority: P3)

An operator running `grimoire-server status` sees the deployed ref, the tool version and the stack's
health. Two hosts on the same commit can maintain entirely different wikis, so the status report also
names the wiki identity currently steering the deployment.

**Why this priority**: It is an operator-visibility improvement on top of US2, valuable but not required
for the identity mechanism to work.

**Independent Test**: Run `status` on a deployment left at the default and on one with an
instance-specific identity, and confirm the two reports differ in exactly that line.

**Acceptance Scenarios**:

1. **Given** a deployment left at the default identity, **When** `status` runs, **Then** it reports that
   the instance is steered by the shipped default foundation document.
2. **Given** a deployment with an instance-specific identity, **When** `status` runs, **Then** it reports
   that an instance-specific foundation document is in effect, with enough detail (a title or summary
   line, and when it was set) to tell two instances apart.

---

### Edge Cases

- **Foundation document missing, empty or unreadable**: the run fails closed before any wiki write, the
  same way a missing per-agent system prompt already does, and the failure names the foundation document
  specifically rather than "instructions".
- **Instance-specific document hand-edited after the wizard set it**: the wizard's re-run reports the
  document as present and does not overwrite it without an explicit decision; the running system keeps
  using the hand-edited content — hand-editing stays a supported operator action.
- **Deployment upgraded from a build that predates this feature**: no instance-specific document exists,
  so the shipped default applies and the deployment behaves exactly as before the upgrade.
- **Foundation document that contradicts an agent's own role instructions**: composition order is fixed
  and documented so the outcome is at least deterministic and inspectable; reconciling the content is a
  maintainer/operator judgment, not a harness behavior.
- **Foundation document attempting to widen what an agent may do** (write outside its scope, reach a
  path outside its roots, alter guarded-tool policy): the guarded tool boundary and credential scope are
  unaffected by instruction content, and the attempt changes nothing about what the agent is permitted
  to do.
- **Very large operator description**: the wizard's output is an instruction document like any other;
  an unusably large one is an operator-visible problem (surfaced by the same recorded content hash and
  by the agents' own behavior), not a harness failure mode.
- **Wizard invoked on a host with no deployment checkout or no state directory**: it fails with the same
  kind of message the tool's other commands already give, and changes nothing.

## Requirements *(mandatory)*

### Functional Requirements

#### Shared foundation prompt (Part 1)

- **FR-001**: The system MUST define exactly one shared foundation instruction document per instance,
  stating what kind of wiki this instance maintains, what it is for, and the conventions that hold
  across every agent's work.
- **FR-002**: Every agent type (ingest, query, lint) MUST load the shared foundation document in
  addition to its own system prompt on every run.
- **FR-003**: The harness MUST compose the shared foundation document with the agent's own system
  prompt in a fixed order that is identical for all agent types and documented, and MUST pass the
  composed result to the agent as its instruction context.
- **FR-004**: Both documents MUST be loaded verbatim — the harness MUST NOT author, template,
  summarize, reorder or otherwise transform their content.
- **FR-005**: Loading MUST be fail-closed: a missing, unreadable or effectively empty foundation
  document MUST fail the run before any wiki write, with a failure reason naming the foundation
  document.
- **FR-006**: Each run's task record MUST list both loaded instruction documents with their individual
  content hashes, so the exact instruction set a run executed under is recoverable after the fact.
- **FR-007**: Evaluation and replay runs MUST resolve and compose the foundation document by the same
  mechanism as a dispatched run, with no additional operator configuration.
- **FR-008**: The system MUST ship a default foundation document whose content describes the wiki
  Grimoire maintains today — a general, personal-knowledge LLM-wiki — and MUST use it whenever the
  instance has not set one of its own.
- **FR-009**: The per-agent system prompts MUST remain hand-authored, version-controlled product
  content, and content that is true of the whole wiki rather than of one agent's role MUST move out of
  them into the foundation document.
- **FR-010**: The foundation document's content MUST NOT be able to alter what an agent is permitted to
  do: guarded-tool policy, write scopes, path roots and credential scope stay in force unchanged
  regardless of what the document says.

#### Deployment identity wizard (Part 2)

- **FR-011**: `grimoire-server` MUST offer a command that asks the operator one question — keep the
  default knowledge wiki, or maintain a specialised one — and puts the resulting foundation document in
  place for that instance.
- **FR-012**: Choosing the default MUST be a complete, valid outcome that leaves the instance
  byte-identical to one that never ran the wizard.
- **FR-013**: Choosing "specialised" MUST collect the operator's own plain-language description of the
  wiki they want maintained and produce the instance's foundation document from it.
- **FR-014**: The wizard MUST be safe to re-run: when a foundation document is already in place — set by
  an earlier run or hand-edited afterwards — it MUST report what is there and MUST NOT replace it
  without an explicit operator decision to do so.
- **FR-015**: The wizard MUST have a non-interactive form in which every answer is supplied up front,
  producing the same outcome as the interactive run with the same answers.
- **FR-016**: When the wizard would need to prompt and no terminal is attached, it MUST fail promptly
  with a message naming the answer to supply, and MUST NOT hang and MUST NOT change anything.
- **FR-017**: An instance-specific foundation document MUST survive redeployment, rollback and restart
  of the deployment it belongs to.
- **FR-018**: `grimoire-server status` MUST report which wiki identity the running deployment is
  steered by — the shipped default, or an instance-specific document identified well enough to tell two
  instances apart.

#### Declined scope, recorded

- **FR-019**: The per-agent system-prompt generator described in issue #217 MUST NOT be built, in any
  form (runtime, build-time, or one-shot setup tool that rewrites the three agent prompts), and the
  decline MUST be recorded on issue #217 itself so the board stops carrying it as pending work.

### Key Entities

- **Foundation document**: the single per-instance instruction document stating what this wiki is, what
  it is for, and the conventions holding across all agents. Has content, an identity (default vs.
  instance-specific) and a content hash recorded per run.
- **Per-agent system prompt**: the existing hand-authored, version-controlled instruction document for
  one agent's role. Unchanged in its role; loses the whole-wiki statements that move to the foundation
  document.
- **Composed instruction context**: what an agent actually receives — foundation document plus that
  agent's system prompt, in a fixed documented order.
- **Instance identity**: the deployment-level fact of which foundation document is in effect (default or
  instance-specific), reportable by the deployment tool and durable across redeployments.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** *(deterministic harness guarantee)*: 100% of agent runs, of every agent type, load the
  foundation document in addition to the agent's own system prompt, and record both documents with
  their individual content hashes in the run's task record.
- **SC-002** *(deterministic harness guarantee)*: 100% of runs whose foundation document is missing,
  unreadable or effectively empty fail before any wiki write, with a failure reason naming the
  foundation document.
- **SC-003** *(deterministic harness guarantee)*: 100% of runs receive the two documents' content
  verbatim in the documented order — byte-for-byte identical to the files on disk, for every agent type
  and for evaluation and replay runs alike.
- **SC-004** *(deterministic harness guarantee)*: 100% of wizard runs that choose the default leave the
  instance byte-identical to one that never ran the wizard.
- **SC-005** *(deterministic harness guarantee)*: 100% of wizard re-runs against an instance that
  already has a foundation document either leave that document untouched or replace it only under an
  explicit operator decision — never silently.
- **SC-006** *(deterministic harness guarantee)*: 100% of wizard invocations with no terminal attached
  either complete from supplied answers or exit non-zero with a message naming the missing answer;
  none block waiting for input.
- **SC-007** *(deterministic harness guarantee)*: after a redeployment, rollback or restart, 100% of an
  instance's runs still load the instance-specific foundation document that was in place before.
- **SC-008** *(lower-stakes agent judgment, narrative)*: with the shipped default foundation document in
  place, agent behavior is indistinguishable from behavior before this feature — the same wiki, the same
  conventions. Deviations are a single correctable wiki edit and are handled through the user-reported
  correction loop (operator observes behavior via the Hub's signals and the wiki itself, reports it, the
  instruction files are adjusted, the operator verifies), not by a formal eval gate.
- **SC-009** *(lower-stakes agent judgment, narrative)*: with an instance-specific foundation document
  in place, agents' work observably reflects that wiki's stated purpose and conventions. What "reflects"
  means is operator judgment on the resulting wiki, corrected by editing the foundation document —
  again the user-reported correction loop, not an eval threshold.

## Assumptions

- The foundation document is instruction content in the sense of Constitution Principle V: it steers
  agent judgment and is never interpreted by harness code. The harness's entire job is to resolve it,
  load it verbatim, compose it in a fixed order, hash it and record it.
- An operator hand-editing the effective foundation document is a supported action, exactly as
  hand-editing any instruction file is today. The wizard is a convenience for producing one, not the
  only sanctioned way to have one.
- "Byte-identical to a deployment that never ran the wizard" (FR-012) is about the instance's
  instruction content and effective configuration, not about the deployment tool's own operator-state
  bookkeeping, which may record that the wizard ran.
- Issue #137's guard-enforced shapes stay product-owned: the `index.md` catalog-entry line shape, the
  append-only log ordering, and the three per-agent write scopes are enforced at the guarded tool
  boundary and are not weakened, extended or made configurable by this feature. FR-010 states this as a
  requirement rather than leaving it implied.
- `deploy/server/grimoire-server` is an operator helper, not a system component: per the user's explicit
  direction it is delivered under the repository's existing shell-script conventions (its own
  `grimoire-server.test.sh` suite and README), and is not subject to the backend's architecture-test,
  observability-contract and eval requirements. The backend half of this feature (Part 1) carries the
  full constitutional obligations.
- The eval runner and replay path already resolve agent instructions from the build-distributed agent
  artifacts without operator configuration (ADR-043); this feature keeps that property rather than
  introducing an eval-specific resolution rule.
- Where the shared document physically lives, and how an instance-specific one reaches a containerized
  deployment, are deliberately left to `/speckit-plan` — the user's description names both candidate
  shapes and requires both to be weighed. This spec constrains the outcome only through FR-007, FR-008
  and FR-017.
