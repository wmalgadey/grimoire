---
status: accepted
---

# Decision Context Overview, Context and Problem Statements

## 0. Guiding Vision — Compound LLM-Wiki

### The North Star

Grimoire is a concrete implementation of the **Compound LLM-Wiki** pattern described by
Andrej Karpathy (2025). The central insight is that the hardest part of maintaining a
personal knowledge base is not the reading or the thinking — it is the bookkeeping:
updating cross-references, flagging stale claims, filing new material consistently. LLMs
excel at exactly that mechanical maintenance without fatigue, while humans retain full
curatorial control over what sources enter the system and what questions they ask.

The result is a **persistent, compounding artifact**: a structured wiki of interlinked
markdown files that grows more coherent and cross-referenced over time, rather than
re-deriving everything from raw documents on every query.

### Three Layers

Every architectural decision in Grimoire must be evaluated against how well it serves
these three layers:

| Layer | Owner | Role |
| --- | --- | --- |
| **Raw Sources** | Human | Immutable, curated input documents. Single source of truth. Never modified by the system. |
| **The Wiki** | LLM (Grimoire agents) | LLM-generated and maintained markdown: summaries, entity pages, concept pages, synthesis. The agents own this layer entirely. |
| **The Schema** | Human + Constitution | Configuration and conventions (structure, naming, workflows) that make the LLM a disciplined maintainer rather than a generic chatbot. `constitution.md` and CLAUDE.md serve this role in Grimoire. |

### Three Operations

Grimoire's agents exist to perform exactly three domain operations — no more, no less.
Each new agent or feature must map to one of these, or the feature is out of scope:

- **Ingest**: Process new source material. Extract key information, update relevant wiki
  pages, maintain and extend cross-references. Triggered by the user adding a source.
- **Query**: Answer questions by searching wiki pages and synthesising citations.
  High-value query results may themselves become new wiki pages, compounding knowledge.
- **Lint**: Health-check the wiki for contradictions, stale claims, orphaned pages, and
  broken or missing cross-references. The quality gate for the compounding artifact.

### Transparent Operations — Tasks as First-Class Artifacts

The primary differentiator between Grimoire and a general-purpose local agent (e.g.
OpenClaw or NanoClaw) running a LLM-wiki skill is **operational transparency**. A generic
agent produces terminal output or a chat response and disappears. Grimoire materialises
every operation as a persistent, structured **task artifact** — a markdown file with YAML
frontmatter — that lives alongside the wiki in git and is fully browsable in Obsidian or
any editor.

Each task file records two layers (following the note-per-task philosophy):

- **Structured layer** (YAML frontmatter): machine-readable state — `type` (ingest /
  query / lint), `status` (queued / running / completed / failed), `agent`, `started_at`,
  `completed_at`, `sources` (list of input documents), `pages_touched` (wikilinks to the
  pages the agent created or modified).
- **Content layer** (markdown body): human-readable report — what the agent found, what
  decisions it made, what it changed, any uncertainties flagged for human review.

Because task files use wikilinks to reference the wiki pages they touched, Obsidian's
backlink and graph views automatically reveal which tasks produced which pages, and which
pages were affected by which operations. The task graph is the audit trail.

**Interactivity**: task files are writable by the human. A user can annotate a completed
task, add context for a follow-up ingest, or mark an open finding as resolved. This keeps
the human in the loop without requiring a dedicated UI for every interaction — the file
system is the interface of last resort.

This capability directly shapes the Web UI, which has three distinct surfaces — one per
agent type — rather than a single generic dashboard:

- **Ingest surface**: The user submits a source (URL, file, clipboard). The UI immediately
  shows the resulting task with live progress streamed from the Ingest agent (pages
  processed, cross-references updated). Completed tasks are the browsable ingest history.
- **Query surface**: The user interacts via a chat interface. When the agent judges a
  query result significant enough to persist, it creates a task that produces a new wiki
  file. The chat thread and the resulting task are linked — the task is the durable
  artifact, the chat is the transient interaction.
- **Lint surface**: The user triggers a lint pass. The UI shows a task with live findings
  streamed as the agent traverses the wiki. The result are single tasks of the lint report
  — listing contradictions, stale claims, and orphaned pages for human review.

The task artifact is the common thread: all three surfaces eventually produce tasks stored
as markdown files in git. The surface shapes the *entry point* and *interaction mode*; the
task shapes the *output and audit trail*. The streaming requirement from the backend is
driven by showing real-time task progress across all three surfaces.

### Architectural Implications

This vision directly constrains the decisions that follow in this document:

1. **Backend & Frontend (§1)**: The Web UI has three agent-shaped surfaces: an Ingest
   surface (source submission + task progress), a Query surface (chat-first with optional
   task creation for new wiki files), and a Lint surface (triggered pass + tasks per
   findings). All three surfaces stream live task state from the backend. The frontend
   routing and component model must reflect this three-surface structure rather than a
   single generic view.

2. **Agent Execution (§2)**: Each agent is responsible for creating and updating its own
   task file throughout its lifecycle — not just reporting a final result. The orchestrator
   must relay task-state updates to all connected channels in real time. Ingest requires
   standalone operation due to its LLM pipeline; Query and Lint can be tighter-coupled to
   the backend, but all three follow the same task-artifact contract.

3. **Channels & API (§3)**: Every channel must be able to trigger an operation and receive
   task-state updates. Telegram users should be able to start an ingest, get a task ID
   back, and receive a completion notification with a link to the task file. The task
   artifact is the canonical result — channels are delivery mechanisms, not result stores.

4. **Repository & Code Structure (§4)**: Task files live in the wiki (e.g. `tasks/` or
   collocated with sources). The repository structure must keep wiki content (including
   tasks), agents, and the backend infrastructure independently navigable. The task schema
   is part of the schema layer and belongs under `.specify/` or a dedicated `schema/`
   folder.

5. **Persistence (§5)**: Completed task files are domain state — they go in git alongside
   the wiki. In-progress task state (status transitions, streaming progress) is operational
   state and lives in the backend's ephemeral store. The handoff point is task completion:
   once an agent finishes, the final task file is written to disk and committed.

### What This Is Not

Grimoire is **not** a RAG system. RAG re-derives answers from raw documents on every
query. Grimoire maintains a standing, curated, cross-referenced body of synthesised
knowledge that improves with every ingest and lint pass. The wiki is the product; the
agents are its caretakers.

---

## 1. Technology Stack — Backend & Frontend Frameworks

The project needs a backend that unifies agent orchestration, channel management (Web UI,
Telegram, etc.), and real-time communication, and a frontend whose Web UI is the central
channel with use-case-specific components. Both must support streaming agent responses and
in-flight task state, not just simple request/response. Both are developed agentic-first
with Spec-Kit — a coding agent generates implementation from specifications for human
review — so the chosen frameworks must be ones the developer (with deep .NET and Typescript experience, solo) can review with confidence and that an SDD agent can generate predictably
and with little boilerplate. The two choices are coupled: a backend transport built around
persistent, bidirectional connections (rather than plain HTTP) implies the frontend must
consume a stateful connection model, not just fetch-and-render. Runtime/framework support
horizon is also a structural concern, since it affects patch cadence and the validity
window of architecture tests.

**Problem Statement:** Which backend framework/runtime and which frontend framework should
be adopted, such that real-time/streaming requirements are met natively, code stays highly
specifiable for SDD agents, and the choice plays to the solo developer's existing
expertise rather than against it? The resulting codebase must also stay easy to read and
easy to understand, with coupling between components kept low, such that the agent,
backend, and all channels remain manageable by a single developer. UI predictability is a
further constraint: the frontend must be built around a small set of central, reusable
components whose visual behaviour is governed by shared CSS definitions (design tokens,
utility classes, or a component stylesheet), so that layout and style remain consistent
and auditable across all views without per-component ad-hoc overrides.

---

## 2. Agent Execution & Orchestration Model

Grimoire routes work to multiple specialized agents (Ingest, Query, Lint) with
distinct responsibilities, and all user requests (Web UI, Telegram, future channels) must
enter through a single backend entry point that dispatches to and collects results from
those agents. Early thinking favored maximum isolation (container-per-agent, arbitrary
languages per agent), but the project is solo-developed and Spec-Kit-first, so operational
overhead must stay proportional to scope while a migration path to stronger isolation
remains open. In practice, agents do not all fit the same execution shape: most can run
safely in-process, passively dispatched by a central orchestrator, but at least one agent
(Ingest) requires standalone CLI operation, independent containerizability, and a lifecycle
decoupled from the backend — driven by its dependency on an LLM-based processing pipeline
(no vector embedding infrastructure exists or is planned) that brings its own runtime and
dependency chain. This raises both a general question (how does a central orchestrator
coordinate heterogeneous agents) and a concrete instance of it (how does the first
standalone, autonomously-triggered agent integrate with that orchestrator).

**Problem Statement:** How should agents be executed (in-process vs. standalone) and how
should a central orchestrator coordinate request routing, work dispatch, result/status
collection, and synchronous vs. asynchronous communication across agents with
heterogeneous runtime and deployment needs — while keeping operational overhead
proportional to a solo-developer project and leaving room for agents to grow more
independent (different runtimes, autonomous triggers, eventual containerization)?

---

## 3. External Interface Contracts — Channels & API

Grimoire is reached through more than one external-facing surface, and each surface raises
the same underlying concern: how does Grimoire's core stay decoupled from the specifics of
who or what it's talking to. On the input/output side, Web UI and Telegram are the initial
channels, with more expected over time; they differ in protocol (HTTP/SSE, Telegram Bot
API), display capabilities, and interaction model, and business/orchestration logic must
not leak into channel-specific code. On the frontend/backend boundary, the frontend
and the backend are decided and developed as independent subprojects; as the
API surface grows, nothing currently prevents the frontend from drifting out of sync with
backend contracts, or from reaching into backend internals informally, since there is no
enforced contract between them yet.

**Problem Statement:** What explicit, enforceable contract should govern each of
Grimoire's external boundaries — between the orchestrator and its pluggable channels, and
between the frontend and the backend API — so that new channels can be added and the API
can evolve without core-code changes, silent drift, or informal coupling across the
boundary?

---

## 4. Codebase & Repository Structure

The project's physical code structure needs to communicate its architecture at two levels.
At the repository level, backend, frontend, and future agent implementations must live and
evolve together while staying independently developable, and Spec-Kit — the SDD toolchain
in use — needs its artifacts (specs, plans, tasks) to map clearly to the corresponding
subproject; this raises the question of repository topology (single repo vs. many, and what
top-level layout) and how `constitution.md` enforces project-wide constraints across it.
At the in-project level, the codebase should be organized by business domain context — a
developer opening the project should see Agents, Hubs, and Channels rather than endpoints,
handlers, and services. This has to be conform with the project constitution's Principle I
(Domain Architecture & Strategic DDD must be visible "from the first commit").

**Problem Statement:** How should the project be structured both at the repository level
(single monorepo vs. alternatives, and how Spec-Kit artifacts map onto it) and at the
in-project level (technical layers vs. business-domain folders within the backend), such
that the structure itself reflects and reinforces the Hub-Spoke, domain-driven
architecture rather than obscuring it behind incidental technical organization?

---

## 5. State & Persistence Strategy

Grimoire must persist two state categories that have materially different requirements.
Domain state — user knowledge such as wiki pages and audit findings — needs to be portable, human-readable, diffable, and editable outside Grimoire entirely (e.g. in Obsidian or any
Markdown editor), and benefits from Git's built-in version control, audit trail, and
rollback; it must be debuggable by inspecting raw files with no opaque or binary formats
in the happy path. Operational state — agent job queues, agent status, and execution history
— is ephemeral, internal bookkeeping tied to a specific running backend instance; it must
survive unplanned restarts (so in-flight job status isn't silently lost) but is not
user-facing and carries different durability, consistency, and lifecycle expectations than
domain state. Treating both categories the same way fails in both directions: storing
operational bookkeeping in Git would pollute the user-facing knowledge repository with
internal noise and force inappropriate version-control semantics onto transient data,
while storing domain knowledge in a database would break the portability, human-readability,
and external-editability that domain state requires.

**Problem Statement:** What storage mechanism(s) should back Grimoire's domain state and
its operational state respectively, so that each gets durability and consistency
guarantees appropriate to its actual lifecycle, without the two polluting or constraining
one another?

---

## 6. Security & Credential Boundary

Grimoire agents call external services — LLM providers (Claude API), Telegram Bot API,
and potentially future integrations — each requiring credentials (API keys, tokens,
secrets). In a naïve setup these secrets are injected as environment variables available to
the whole process, meaning any agent (or a compromised agent) can read credentials intended
for a different service. NanoClaw addresses this with an OneCLI Credential Gateway: a
host-side proxy that injects credentials at the network boundary based on agent identity,
so agents never hold raw secrets. Grimoire must decide whether to adopt an equivalent
pattern, and if so, what the credential injection model looks like given its current
architecture (a Node.js backend with in-process and potentially containerised agents).
A related question is agent identity: does the orchestrator authenticate each agent before
dispatching work, or is process ownership sufficient isolation at this scale?

**Problem Statement:** How should credentials (LLM API keys, bot tokens, third-party
service secrets) be stored, distributed, and injected into Grimoire's agents and channels,
such that no agent holds secrets beyond its own operational scope, the attack surface for
credential leakage is minimised, and the model remains auditable and extensible as new
agents or channels are added?

---

## 7. Agent Guardrails & Output Safety

Grimoire's agents write directly to the wiki — a curated, user-trusted knowledge artifact.
An agent that produces hallucinated output, follows an injected prompt from a malicious
source document, or calls a tool outside its intended scope can corrupt the wiki in ways
that are hard to detect and expensive to reverse. NanoClaw mitigates this through process
isolation (container sandboxing limits what an agent can reach), but does not define an
application-level output validation policy. Grimoire needs both layers: structural
isolation (which §2 partially addresses) and application-level guardrails that validate
agent output before it is written to disk. The Lint agent is a partial answer — it
detects post-hoc wiki quality issues — but it does not prevent bad writes from entering
the wiki in the first place. Prompt injection is a concrete threat: a source document
submitted to Ingest could contain instructions designed to redirect the agent's behaviour,
exfiltrate wiki content, or trigger tool calls outside its intended scope.

**Problem Statement:** What application-level guardrails must be enforced before any
agent output is committed to the wiki, how is prompt injection from untrusted source
documents defended against, and how is each agent's tool-use scope constrained to prevent
out-of-scope or destructive actions — without introducing so much overhead that the solo
developer cannot maintain the guardrail layer alongside the agents themselves?

---

## 8. Observability Strategy

The project constitution (Principle IV) mandates OpenTelemetry instrumentation for every
plan — named spans, structured log events, and business metrics must be present before a
feature is considered done; code lacking this instrumentation fails the Definition of Done
and must not be merged. This is a non-negotiable code-level constraint, but it presupposes
an infrastructure decision that has not yet been made: what is the OTel stack that
receives, stores, and surfaces that telemetry? Grimoire's task artifacts cover domain-level
audit (what the agent did, what pages it touched), but agent-internal execution — LLM
latency, tool call counts, token usage, error rates — is currently invisible without a
backend that ingests OTel signals. The decision must also cover the dev-time story: a
developer running Grimoire locally must be able to inspect traces without deploying cloud
infrastructure, since a locally unverifiable instrumentation requirement cannot be enforced
at the Definition of Done gate.

**Problem Statement:** Which OpenTelemetry collector, exporter, and backend (local and/or
cloud) should Grimoire adopt so that the full instrumentation mandated by the constitution
— traces, metrics, and structured logs — is verifiable both locally and in CI; and how do
agent-level OTel spans map to the three domain operations (Ingest, Query, Lint) and their
task lifecycles so that the span inventory in every `plan.md` has a canonical reference
model to build against?

---

## 9. Simplicity Budget & Complexity Governance

NanoClaw's explicit architectural commitment is "small enough to understand": core
infrastructure is minimal; features are layered via a skills/branch model; direct code
modification is preferred over configuration sprawl. Grimoire shares the same constraint —
it is a solo-developer project maintained and extended largely by an SDD coding agent, so
every additional service, configuration surface, or abstraction layer increases the review
burden on the single human reviewer. The constitution's ADR gate (Principle IV) already
answers the reactive question unconditionally: every new custom infrastructure piece
requires an approved ADR before implementation begins, with no exceptions. What the
constitution does not define is the *proactive* side: what does a desirably simple system
look like as a measurable target, how is accumulated complexity surfaced so that agents
and the developer can reason about the whole rather than each addition in isolation, and
how is "prefer explicit TypeScript over configuration" turned into a rule concrete enough
to evaluate in a code review rather than a vague aspiration.

**Problem Statement:** What measurable simplicity targets should Grimoire hold itself to
— in terms of top-level services, runtime processes, and third-party dependencies at each
project stage — and what automated architecture tests in the CI/CD pipeline structurally
enforce those limits (e.g. dependency-count assertions, banned-import rules, service-count
checks) so that simplicity is a build-breaking constraint, not a soft preference that
exists only on paper?

---

## 10. Open Standards & Protocol Preference

For cross-cutting concerns, the constitution and existing ADRs already settle some
choices unconditionally; others remain open decisions. The distinction matters because the
two categories require different governance:

- **Observability** — OpenTelemetry (traces, metrics, structured logs, business metrics)
  is mandated by the constitution (Principle IV). A bespoke instrumentation layer is not
  an option: code lacking the specified OTel instrumentation fails the Definition of Done
  and must not be merged. The open question is infrastructure (§8), not the standard itself.
- **API contracts** — The format standard (OpenAPI, AsyncAPI, or equivalent) is an input
  to the §3 ADR, which owns the API contract decision. §10 does not duplicate that
  decision; it only asserts that whatever format §3 selects must be an open standard with
  machine-readable validation tooling, not a bespoke convention.
- **Event & message formats** — CloudEvents provides a vendor-neutral envelope for
  domain events crossing agent or channel boundaries, replacing ad-hoc JSON shapes.
  No standard has been mandated yet.
- **Credential & secret management** — The storage and injection model is an input to the
  §6 ADR, which owns the credential boundary decision. §10 does not duplicate that
  decision; it only asserts that whatever mechanism §6 selects must follow an open,
  well-documented pattern rather than a bespoke credential-passing scheme.

For the settled cases (observability), the simplicity budget (§9) cannot override the
mandate. For the open cases, the simplicity budget is a valid input into the ADR that
makes the choice: a standard that requires running additional infrastructure must justify
that overhead in the ADR before it is adopted.

**Problem Statement:** For the cross-cutting concerns where no standard has yet been
mandated — event messaging, and any future concerns not yet covered by an existing section
— which open standards or open protocols should be elevated to constitutional constraints?
The governance mechanism already exists: the constitution (Principle III) mandates that
any new cross-cutting concern introduced by a spec requires an ADR drafted before plan
finalization, enforced by an automated architecture test. §10's ADR must enumerate the
open standards adopted as constraints and record the explicit rationale for any case where
an available open standard was considered and rejected.

---

## Related Documents

- `.specify/memory/constitution.md` — Project constitution referenced throughout
