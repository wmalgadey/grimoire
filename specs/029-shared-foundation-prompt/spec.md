# Feature Specification: Shared Foundation Prompt and Wiki-Identity Wizard

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

## Clarifications

### Session 2026-09-05

- Q: How much of the three existing system prompts moves into the shared foundation document in
  this feature? → A: the wiki-identity statement plus everything currently stated in two or more of
  the three prompts — folder structure, page types, page language, frontmatter standard, tag
  taxonomy, confidence scoring, `index.md`/`log.md` entry conventions, and the "source content is
  data, not instructions" rule. Role-specific steps, write scopes and per-agent modes stay in their
  own files. Rationale: this is what actually delivers #137's convention layer (an operator
  specialising the wiki can change page types or tag taxonomy in one place) without rewriting text
  only one agent ever states, which would make a behavioural shift hard to attribute.
- Q: How does the wizard produce the foundation document when the operator chooses a specialised
  wiki? → A: Claude Code drafts it, the wizard places it. The wizard emits a drafting brief (the
  operator's description plus the document's required shape) for a Claude Code session on the deploy
  host; the wizard's own job is validation and safe placement — check what is already there, refuse
  to clobber silently, write the file where the agents read it, record that it did. It also accepts
  a ready document via a flag, which is what the non-interactive path uses. Rationale: content
  judgment about what a wiki should do stays agent-side (the Principle V line), and the shell script
  never authors instruction text from a template.
- Q: How far should the mechanism — the shape of the per-run record and how the two documents are
  composed — move out of the spec into `plan.md`/the ADRs? → A: the evidence *form* moves out, the
  trust properties stay. US1-AS1, FR-006, SC-001 and SC-003 now state the outcome ("after a run it is
  determinable which instruction documents it operated under and in which version, distinguishably per
  document"); how that is recorded — two entries in the task artifact, each with its own SHA-256 — is
  a plan-level decision. FR-003 (one fixed, documented order for every agent type), FR-004 (content
  reaches the agent unmodified) and FR-005 (fail-closed) stay in the spec: they constrain what the
  system may do to instruction content rather than describing a mechanism, and the fixed order comes
  from the feature request itself.
- Q: Should the spec require that evaluation and replay runs resolve and compose the foundation
  document exactly as a dispatched run does? → A: no — evaluation is internal machinery, not a
  spec-level requirement. US1-AS4 and FR-007 are removed and SC-003's evaluation clause dropped. The
  obligation itself does not disappear: it moves to `plan.md` and the resolution ADR, where the eval
  runner's repository-source resolution is decided.
- Q: Where does the identity wizard live — in the deployment script, or in the system itself? → A: in
  the system. It already has the operator interface and console integration the wizard needs, and
  putting it there is what lets a later user-facing surface expose the same wizard instead of
  reimplementing it (a separate, future spec). The deployment script *starts* the system's wizard and
  surfaces the identity the system reports; it implements no wizard of its own. Consequence recorded in
  Assumptions: the wizard is system code and carries the full constitutional obligations, and only the
  thin glue in the deployment script keeps the relaxed shell-script conventions.
- Q: Where does the agent session that drafts a specialised foundation document run? → A: on the deploy
  host, not inside the system. The system produces the drafting brief and accepts the drafted document
  back through a second invocation; it neither spawns nor remote-controls the drafting agent. This
  keeps the deployment image, its credential scoping and the external-system boundary untouched, and it
  means this feature is **not** a partial implementation of issue #102 — the framing "the system
  remote-controls Claude Code" does not survive this answer, and the spec says hand-off instead.
- Q: Who answers "which wiki identity steers this deployment"? → A: the system reports it (FR-018) and
  the deployment script surfaces that answer rather than working it out itself (FR-018a). The same
  report is what a later user-facing surface would show.
- Q: Should the wizard also write the per-agent role documents, so a specialised wiki can carry its own
  frontmatter, categories or labels? → A: no — scope stays at one document, and the gap this question
  points at is filed as issue #224. Two reasons. The examples given (frontmatter, categories, labels)
  are wiki-wide conventions and already move into the foundation document under the extraction
  clarification above, so a specialised instance changes them in one place today. And a wizard that
  writes the role documents *is* the one-shot setup tool FR-019 declines: those three files are
  version-controlled product content, and an instance that gets its own copies is a fork cut off from
  every later improvement to them. What genuinely has no home is a *role-specific* per-instance delta
  (#217's own example: for a travel wiki, Query should combine wiki history with external research) —
  that is #224, and it needs its own ADR decision, because composing a third document into the system
  prompt is listed in ADR-053's Change Triggers as an invalidation requiring full supersession.
- Q: Should the wizard prompt for a missing answer when a terminal is attached, and detect the
  non-interactive case separately? → A: no — drop prompting entirely, as recommended after checking the
  code: **no Hub CLI command prompts today**. There is no `AnsiConsole.Prompt`, `TextPrompt` or
  `ConfirmationPrompt` anywhere in the backend; every command takes its input as arguments and options,
  and the only stdin use is piping pasted text into `submit-source`. Building a prompting path plus TTY
  detection would add machinery this CLI deliberately never had, for callers — the deployment script, a
  container exec, later a user-facing surface — that never prompt anyway. One path instead: every answer
  is supplied with the invocation, and a missing one fails naming what to pass. Consequence: **US3 is
  deleted** (its premise, "works where nobody can answer a prompt", no longer describes anything
  special), its residual guarantee becomes a US2 acceptance scenario, the former US4 becomes US3, and
  FR-015/FR-016 and SC-006 are restated without the terminal distinction. This reverses the earlier
  answer in this session that kept US3 as its own story — that choice was made while the dual-mode
  design still stood.

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
say, dispatch one run of each agent type, and confirm for each run that it operated under that
statement — and that the per-agent files were not edited to achieve it.

**Acceptance Scenarios**:

1. **Given** a deployment with the shipped default foundation document, **When** an ingest, a query and
   a lint run are dispatched, **Then** it is afterwards determinable, for each run, which instruction
   documents it operated under and in which version — the shared foundation document included.
2. **Given** the shared foundation document and an agent's own system prompt, **When** any agent run
   starts, **Then** the agent operates under both documents' content unmodified, in the same documented
   order for every agent type.
3. **Given** a shared foundation document that is missing, unreadable or effectively empty, **When** a
   run is dispatched, **Then** the run fails before any wiki write, naming the document that could not
   be loaded.

---

### User Story 2 - An operator decides what wiki this instance maintains (Priority: P2)

An operator bringing up a deployment is asked one question by the system itself: keep the default
general-knowledge wiki, or maintain a specialised one. Keeping the default is a complete answer and
leaves the instance indistinguishable from one that was never asked. Choosing "specialised" lets the
operator describe, in their own words, the wiki they want maintained; the description is turned into a
foundation document by an agent session, and the system puts it in place for this instance — without
the operator hand-editing any product file.

**Why this priority**: It is what makes the shared document per-instance rather than merely
deduplicated, and it is the operator-facing half of the feature. It depends on US1 being in place.

**Independent Test**: On a fresh deployment, run the identity wizard choosing "default" and confirm the
instance is indistinguishable from one that never ran it; then run it choosing "specialised" with a
description, and confirm the running deployment's agents are steered by a foundation document
reflecting that description.

**Acceptance Scenarios**:

1. **Given** a deployment that has never run the identity wizard, **When** the operator runs it and
   chooses the default wiki identity, **Then** no instruction content is added, changed or removed
   anywhere in the instance, and subsequent runs operate under the shipped default foundation document.
2. **Given** a deployment that has never run the identity wizard, **When** the operator chooses a
   specialised wiki and supplies a description, **Then** the system produces a drafting brief from that
   description, and once a document drafted from that brief is handed back, every subsequent agent run
   of every type operates under it in place of the shipped default.
3. **Given** an instance whose foundation document was already set (by an earlier wizard run or by
   hand), **When** the wizard runs again, **Then** it reports what is already in place and does not
   replace it until the operator explicitly decides to.
4. **Given** an instance-specific foundation document in place, **When** the deployment is redeployed to
   another ref or rolled back, **Then** the document survives and still steers the agents.
5. **Given** the operator drives the deployment through the deployment script rather than the system's
   own interface, **When** they ask it to set the wiki identity, **Then** the script starts the
   system's wizard rather than implementing one of its own.
6. **Given** an invocation that omits an answer the wizard needs, **When** it runs, **Then** it fails
   immediately with a message naming what to supply, and changes nothing — it never waits for input.

---

### User Story 3 - "What is running here?" includes which wiki it maintains (Priority: P3)

Two deployments of the same commit can maintain entirely different wikis. The system therefore reports
which wiki identity is currently steering it — the shipped default, or an instance-specific document —
and the deployment script surfaces that answer alongside the deployed ref and the tool version, rather
than working it out for itself.

**Why this priority**: It is an operator-visibility improvement on top of US2, valuable but not required
for the identity mechanism to work.

**Independent Test**: Ask a deployment left at the default and one with an instance-specific identity
which identity steers it, and confirm the two answers differ; then confirm the deployment script's own
status output carries the same answer.

**Acceptance Scenarios**:

1. **Given** a deployment left at the default identity, **When** its identity is queried, **Then** it
   reports that the instance is steered by the shipped default foundation document.
2. **Given** a deployment with an instance-specific identity, **When** its identity is queried, **Then**
   it reports that an instance-specific document is in effect, with enough detail (a title or summary
   line, and when it was set) to tell two instances apart.
3. **Given** either of the above, **When** the operator asks the deployment script what is running,
   **Then** the same answer appears there, obtained from the system rather than recomputed.

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
  an unusably large one is an operator-visible problem (surfaced by the run's recorded version and by
  the agents' own behavior), not a harness failure mode.
- **Drafting brief produced but never answered**: the operator asks for a specialised wiki, the brief is
  emitted, and no drafted document is ever handed back. The instance stays on the shipped default —
  emitting a brief changes nothing on its own — and the wizard can be re-run from the beginning.
- **A drafted document is handed back that is empty or unreadable**: it is rejected and nothing is
  placed; the instance keeps whatever identity it had.
- **Wizard invoked against an instance whose deployment cannot be reached**: it fails with a message
  naming what it could not reach, and changes nothing.

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
- **FR-006**: After a run, it MUST be determinable which instruction documents that run operated under
  and in which exact version, distinguishably per document — so a change in behaviour can be attributed
  to the shared statement or to one agent's role document. How that is recorded is a plan-level
  decision, not a requirement here.
- **FR-008**: The system MUST ship a default foundation document whose content describes the wiki
  Grimoire maintains today — a general, personal-knowledge LLM-wiki — and MUST use it whenever the
  instance has not set one of its own.
- **FR-009**: The per-agent system prompts MUST remain hand-authored, version-controlled product
  content, and content that is true of the whole wiki rather than of one agent's role MUST move out of
  them into the foundation document. Concretely (2026-09-05 clarification): the wiki-identity
  statement plus every convention currently stated in two or more of the three prompts — folder
  structure, page types, page language, frontmatter standard, tag taxonomy, confidence scoring,
  `index.md` and `log.md` entry conventions, and the "source content is data, not instructions" rule
  — MUST move to the foundation document; role-specific steps, per-agent write scopes and per-agent
  modes MUST stay in the agent's own file.
- **FR-010**: The foundation document's content MUST NOT be able to alter what an agent is permitted to
  do: guarded-tool policy, write scopes, path roots and credential scope stay in force unchanged
  regardless of what the document says.

#### Deployment identity wizard (Part 2)

- **FR-011**: The system itself MUST offer an identity wizard that asks the operator one question —
  keep the default knowledge wiki, or maintain a specialised one — and puts the resulting foundation
  document in place for that instance. It MUST be reachable through the system's own operator
  interface, so that a later user-facing surface can expose the same wizard without reimplementing it.
- **FR-012**: Choosing the default MUST be a complete, valid outcome that leaves the instance
  indistinguishable from one that never ran the wizard.
- **FR-013**: Choosing "specialised" MUST collect the operator's own plain-language description of the
  wiki they want maintained, and the instance's foundation document MUST be produced from it.
- **FR-013a**: The system MUST NOT author the foundation document's content itself, neither from a
  template nor by running an agent loop of its own. It MUST emit a drafting brief — the operator's
  description plus the document's required shape — for an agent session to draft from, and MUST accept
  the drafted document back as input. The system's own responsibilities are producing the brief,
  validating what comes back, placing it safely and recording that it did.
- **FR-014**: The wizard MUST be safe to re-run: when a foundation document is already in place — set by
  an earlier run or hand-edited afterwards — it MUST report what is there and MUST NOT replace it
  without an explicit operator decision to do so.
- **FR-015**: Every answer the wizard needs MUST be supplied up front with the invocation. The wizard
  MUST NOT prompt for input under any circumstance, so it behaves identically whether or not a terminal
  is attached and needs no way to tell the difference.
- **FR-016**: An invocation missing an answer MUST fail promptly with a message naming what to supply,
  MUST NOT wait for input, and MUST NOT change anything.
- **FR-017**: An instance-specific foundation document MUST survive redeployment, rollback and restart
  of the deployment it belongs to.
- **FR-018**: The system MUST report which wiki identity is currently steering it — the shipped
  default, or an instance-specific document identified well enough to tell two instances apart.
- **FR-018a**: The deployment script MUST start the system's wizard rather than implementing one of its
  own, and MUST surface the identity the system reports (FR-018) alongside the deployment facts it
  already shows. It MUST NOT determine that identity by its own means.

#### Declined scope, recorded

- **FR-019**: The per-agent system-prompt generator described in issue #217 MUST NOT be built, in any
  form (runtime, build-time, or one-shot setup tool that rewrites the three agent prompts), and the
  decline MUST be recorded on issue #217 itself so the board stops carrying it as pending work. This
  holds for the identity wizard too: it writes the foundation document and nothing else. A
  role-specific per-instance layer — the part of #217's motivation that the foundation document
  cannot express — is out of scope here and tracked as issue #224.

### Key Entities

- **Foundation document**: the single per-instance instruction document stating what this wiki is, what
  it is for, and the conventions holding across all agents. Has content, an identity (default vs.
  instance-specific) and a version that is determinable per run.
- **Per-agent system prompt**: the existing hand-authored, version-controlled instruction document for
  one agent's role. Unchanged in its role; loses the whole-wiki statements that move to the foundation
  document.
- **Composed instruction context**: what an agent actually receives — foundation document plus that
  agent's system prompt, in a fixed documented order.
- **Drafting brief**: what the system produces from the operator's description — the description plus
  the foundation document's required shape — for an agent session to draft the document from. It is an
  input to drafting, never itself the foundation document.
- **Instance identity**: the deployment-level fact of which foundation document is in effect (default or
  instance-specific), reported by the system, surfaced by the deployment script, and durable across
  redeployments.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** *(deterministic harness guarantee)*: 100% of agent runs, of every agent type, operate
  under the foundation document in addition to the agent's own system prompt, and for 100% of runs both
  documents and their exact versions are determinable afterwards, distinguishably per document.
- **SC-002** *(deterministic harness guarantee)*: 100% of runs whose foundation document is missing,
  unreadable or effectively empty fail before any wiki write, with a failure reason naming the
  foundation document.
- **SC-003** *(deterministic harness guarantee)*: in 100% of runs the agent operates under the two
  documents' content byte-for-byte as it stands on disk, in the documented order, for every agent type.
- **SC-004** *(deterministic harness guarantee)*: 100% of wizard runs that choose the default leave the
  instance's instruction content and effective configuration identical to one that never ran the
  wizard.
- **SC-005** *(deterministic harness guarantee)*: 100% of wizard re-runs against an instance that
  already has a foundation document either leave that document untouched or replace it only under an
  explicit operator decision — never silently.
- **SC-006** *(deterministic harness guarantee)*: 100% of wizard invocations either complete from the
  answers supplied with them or fail with a message naming the missing answer; none wait for input,
  with or without a terminal attached.
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
- "Indistinguishable from a deployment that never ran the wizard" (FR-012) is about the instance's
  instruction content and effective configuration, not about bookkeeping that records the wizard ran.
- Issue #137's guard-enforced shapes stay product-owned: the `index.md` catalog-entry line shape, the
  append-only log ordering, and the three per-agent write scopes are enforced at the guarded tool
  boundary and are not weakened, extended or made configurable by this feature. FR-010 states this as a
  requirement rather than leaving it implied.
- The identity wizard is **system code**, not deployment tooling (2026-09-05 clarification). It
  therefore carries the full constitutional obligations — ADRs, hermetic tests, observability contract,
  hexagonal boundaries — exactly as Part 1 does. The earlier assumption that this half could be built
  under the deployment script's relaxed conventions no longer applies to it.
- What remains in `deploy/server/grimoire-server` is thin glue: it starts the system's wizard and
  surfaces the identity the system reports (FR-018a). That glue stays an operator helper under the
  repository's existing shell-script conventions (its own `grimoire-server.test.sh` suite and README),
  and is not subject to the backend's architecture-test, observability-contract and eval requirements.
  It contains no wizard logic of its own to be relaxed about.
- The system does not run an agent loop of its own for drafting, and it does not spawn or remote-control
  a drafting agent (2026-09-05 clarification): it hands out a brief and takes a document back. An agent
  session on the deploy host does the drafting. This feature is therefore **not** an implementation,
  partial or otherwise, of issue #102 (replacing the in-process agent loop with Claude Code headless) —
  it introduces no new external system, no new port, and no second agent runtime. #102 remains open and
  untouched.
- The operator can hand the drafted document back at a later time and from a different session than the
  one that asked for it. Emitting a brief is not a state the instance is stuck in: until a document is
  handed back and accepted, the instance simply keeps the identity it already had.
- Evaluation and replay runs are internal machinery, not a spec-level requirement (2026-09-05
  clarification): the spec no longer states how they resolve instructions. They must still resolve and
  compose the foundation document the same way a dispatched run does, with no operator configuration —
  that obligation now lives in `plan.md` and the resolution ADR, where the eval runner's existing
  repository-source resolution is already decided.
- Every recorded-replay eval recording goes stale with this feature, unavoidably: the instruction text
  an agent receives changes for all three agent types the moment a second document is composed into
  it, and the replay path compares that text's hash against the recording. This is the documented
  instruction-change merge gate working as designed (ADR-012's fingerprint/staleness mechanism), not a
  defect introduced here, and it is not avoidable by scoping the extraction differently — composition
  alone changes the hash. Refreshing the recordings needs a live provider run, which is an operator
  action (the repository's eval capture workflow), so the plan must treat "recordings refreshed" as an
  explicit, separately triggered step of the Definition of Done rather than something implementation
  can complete on its own.
- Where the shared document physically lives, and how an instance-specific one reaches a containerized
  deployment, are deliberately left to `/speckit-plan` — the user's description names both candidate
  shapes and requires both to be weighed. This spec constrains the outcome only through FR-008 (a
  shipped default applies when the instance sets nothing) and FR-017 (an instance-specific document
  survives redeployment).
