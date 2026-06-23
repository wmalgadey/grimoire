## 2026-06-21

```bash
uv tool install specify-cli --from git+https://github.com/github/spec-kit.git@v0.11.3
uv tool update-shell
```

- anlegen dieser datei

```bash
specify init --here
```

- https://github.github.com/spec-kit/community/presets.html

```bash
specify preset add --install-allowed --from https://github.com/0xrafasec/spec-kit-preset-claude-ask-questions/archive/refs/tags/v1.0.0.zip
```

## 2026-06-22

```claude
/speckit.constitution Establish a Spec-Driven, ADR-First development philosophy. Enforce these mandatory constraints for all `/plan` and `/tasks` generations: 

1. **Architecture & DDD:** Reject Big Design Up Front. Implement Strategic DDD (Ubiquitous Language, Bounded Contexts) from day one. Restrict Tactical DDD to the isolated Core Domain. Keep the Domain Core strictly dependency-free.
2. **Pragmatic Testing:** Prioritize integration tests via Testcontainers for API boundaries. Use unit tests exclusively for complex domain logic. Reject dogmatic red-green-refactor for simple DTOs and excessive mocking.
3. **Test-Driven Architecture & ADR-First:** Agents must read `docs/adr/` before planning. `plan.md` must include a dedicated 'Architectural Constraints & ADRs' section referencing applied decisions. Introducing new structural boundaries requires drafting a new MADR first. The very first task in `tasks.md` must always be the implementation of an automated architecture test to enforce the new ADR.
4. **Behavioral & Observable Engineering:** Conventions exist only if enforced by CI/CD. Unapproved custom infrastructure is forbidden. `plan.md` must include an 'Observability' section detailing specific business metrics, structured log events, and distributed trace spans (OpenTelemetry). Code without instrumentation fails the DoD.
```

## 2026-06-23

Überlegungen zu den ADRs

- Techstack?
- Guidelines?
- Architektur Tests --> NetArchTest.Rules/Roslyn
- Was steht dann in CLAUDE.md?

---

- problem: ich weiß nicht, was der ideale techstack ist. ich würde gerne C# nutzen, weiß aber auch, dass das mit LLMs nicht so optimale ergebnisse bringt.
- das habe ich dann mit claude gespiegelt: https://claude.ai/share/d203a4c2-0c0f-46fc-bb49-233df685264c
- daraus habe ich ADRs generieren lassen.
- Danach musste ich aber nochmal constitution.md anpassen und den Plan den ich zwischenzeitlich für die ADRs erstellt hatte, war nicht mehr gültig.

---

- `/speckit.agent-context.update` aktualisiert die CLAUDE.md (hat nur nichts gemacht bei mir!)

---

Was nun? ADRs finde ich gut, constitution.md auch. Aber wie gehe ich jetzt wirklich vor um das Projekt umzusetzen?

```claude
/speckit.specify

We are building an AI Agent Orchestrator platform called "Grimoire".

This spec covers the PROJECT SKELETON ONLY — no features, no business logic.

Scope:
- Monorepo directory structure (src/backend, src/frontend, src/agents, docs/adr/)
- .NET 9 Minimal API + SignalR project scaffold (empty, compiles, no endpoints except health)
- IChannel and IAgentWorker interface definitions (empty contracts only)
- Svelte 5 + Vite frontend scaffold (empty app, compiles, no UI content)
- NetArchTest.Rules setup + architecture tests enforcing ADR-001 through ADR-007
- CI pipeline skeleton (build + architecture tests pass)

Out of scope: Any channel implementation, any agent implementation, any UI components, 
any business logic.

Read constitution.md and all ADRs in docs/adr/ before generating the spec.
```

---

Ein bischen in der spec-kit Doku nachgeschaut, und dann noch den "Git Branching Workflow" hinzugefügt.
- `specify extension add git`

---

Ich würde gerne noch LikeC4 ins Projekt integrieren!! Das gehört aber nicht in einen ADR, sondern in die constitution.md. Hier habe ich aber gerade keine Idee, wie ich das am besten formuliere.

---

```claude
execute the implementation planning
```

---

```claude
führe den nächsten spec-kit schritt aus
```

---

```claude
implement the current plan
```
---

Umsetzung (KI generiert)
- Phase 1 — Hub + erster Agent (Fundament)

    IChannel/IAgentWorker vollständig implementiert — Health-Endpoint, Agent-Lifecycle (Start/Stop/Health), noch kein echter Agent
    Ingest-Agent — Watch auf raw/sources/, Git-Commit nach Verarbeitung, SHA256-Cache für inkrementelles Processing. Das ist der kritischste Agent weil alle anderen auf ingested Content aufbauen
    Web-UI Channel — /ingest — Minimales Form-UI, löst Ingest-Agent aus, zeigt Status

- Phase 2 — Query

    Query-Agent — Liest wiki/, antwortet mit konfigurierbarer Persona aus purpose.md
    Web-UI — /query — Chat-Interface mit SignalR-Streaming

- Phase 3 — Lint

    Lint-Agent — Cron-basiert, schreibt audits/open/*.md im definierten Artefakt-Format
    Web-UI — /audit — Checklist-UI zum Lösen von Audit-Artefakten, Commit zu audits/resolved/

- Phase 4 — Batch + Telegram

    Batch-Agent — Synthese, Connection-Suggestions, schreibt wiki/batch/*.md
    Telegram Channel — Zweiter Channel-Spoke am Hub, ohne Agent-Änderungen

---

Für Phase 1 von claude.ai eine beschreibung des features generieren lassen... Das war eine Menge Text, ist das wirklich so notwendig?

```claude
/speckit.specify

## Context

Project: Grimoire — Wiki-Harness for Zettelkasten/Obsidian knowledge management.
Constitution: Read `constitution.md` before proceeding.
ADRs: Read all ADRs in `docs/adr/` before proceeding.
Architecture: Read LikeC4 model in `docs/architecture/` before proceeding.

Existing foundation:
- `IChannel` and `IAgentWorker` interfaces exist (empty contracts)
- .NET 9 Minimal API project builds successfully
- No implementations exist yet

---

## Spec: Hub Foundation + Agent Lifecycle

### Bounded Context

This spec is scoped to the **Orchestration Bounded Context** only.
No Wiki domain logic. No channel-specific logic. No agent business logic.

Ubiquitous Language established by this spec:
- **Hub**: The central orchestrator that manages agent lifecycle and routes channel requests
- **AgentDescriptor**: Metadata describing a registered agent (id, name, status, capabilities)
- **AgentStatus**: Enum — `Unregistered | Starting | Running | Stopping | Stopped | Faulted`
- **AgentJob**: A unit of work dispatched to an agent (id, agentId, payload, status, timestamps)
- **JobStatus**: Enum — `Pending | Running | Completed | Failed`

---

### User Stories

**US-01: Agent Registration**
As the Hub, I want to register an IAgentWorker implementation at startup
so that the Hub knows which agents are available and can manage their lifecycle.

Acceptance Criteria:
- An agent can be registered with a unique id and descriptor
- Registering the same agent id twice throws a domain exception
- Registered agents are discoverable via the Hub

**US-02: Agent Lifecycle Management**
As the Hub, I want to start, stop, and health-check registered agents
so that I can ensure agents are available before dispatching work.

Acceptance Criteria:
- Hub can start a registered agent (transitions: Unregistered → Starting → Running)
- Hub can stop a running agent (transitions: Running → Stopping → Stopped)
- Hub detects a faulted agent and transitions status to Faulted
- All lifecycle transitions are logged as structured log events

**US-03: Health Endpoint**
As an operator, I want a `/health` endpoint
so that I can verify the Hub and all registered agents are operational.

Acceptance Criteria:
- `GET /health` returns overall Hub status
- Response includes status of each registered agent (id, name, AgentStatus)
- Returns HTTP 200 if Hub is healthy, HTTP 503 if any agent is Faulted
- Response is JSON

**US-04: Hub Operational State (SQLite)**
As the Hub, I want to persist agent job queue and agent status in SQLite
so that operational state survives Hub restarts without polluting the Git-managed Wiki state.

Acceptance Criteria:
- SQLite stores: AgentDescriptors, AgentJobs (with JobStatus and timestamps)
- SQLite is NOT used for Wiki content, audit artefacts, or any domain output — those belong to Git
- Schema is created automatically on first startup (no manual migration step)
- SQLite file path is configurable via config

---

### Out of Scope

- No actual agent implementation (IAgentWorker remains an interface; a NoOpAgent stub is sufficient for lifecycle testing)
- No channel implementation (IChannel remains an interface)
- No job dispatching logic (AgentJob persistence only — no execution)
- No Wiki domain logic (no Git operations, no Markdown, no Zettelkasten)
- No authentication

---

### Architectural Notes for Planner

This spec introduces:
- **Hub as an Application Service** coordinating agent lifecycle — must live in Application layer, not Domain Core
- **SQLite as operational state** (ADR to be drafted: State Strategy — Git for domain artefacts, SQLite for Hub operational state)
- **Structured logging** on all lifecycle transitions is a DoD requirement per constitution.md

The planner MUST:
1. Draft ADR for State Strategy (Git vs. SQLite) before finalizing plan.md
2. Include `## Architectural Constraints & ADRs` section referencing ADR-001, ADR-002, ADR-004, and the new State Strategy ADR
3. Include `## Observability` section with:
   - Business metrics: `grimoire.hub.agents_registered`, `grimoire.hub.agent_status_transitions_total`
   - Structured log events: agent registered, lifecycle transition (with agentId, fromStatus, toStatus), agent faulted
   - Trace spans: `hub.agent.start`, `hub.agent.stop`, `hub.agent.health_check`
4. First task in tasks.md MUST be NetArchTest enforcing that Hub/Application layer does not reference Infrastructure directly
```

---

habe ich security vergessen? wie werden die channels authentifiziert, also das Web UI und die Api des Backends?

---

```claude
check the pr remarks
```

---

mir gefällt der aufbau der api nicht. ich hätte lieber "screaming architecture", bzw. "screaming design" gehabt. also agent-classes im /agents-Ordner und ähnliches.

- Hier wurde eine "constitution" nicht berücksichtigt: "Domain Architecture & Strategic DDD"

```claude
mir gefällt der aufbau der api nicht. ich hätte lieber "screaming architecture", bzw. "screaming design" gehabt. also agent-classes im /agents-Ordner und ähnliches. sprich alle classen nicht anhand von tier bzw. levels unterteilt, sondern anhand der funktionen

...

/speckit-specify refactor den code hin zu einer screaming architecture, inder der die klassen nach domänen organisiert sind und nicht nach Clean Architecture prinzipien
```

Refactoring hat eigentlich gut funktioniert (Codebase war ja auch noch klein), aber die tasks.md wurde nicht abgehakt. Die CLAUDE.md wurde auch nicht auf das neue Feature angepasst!

---

- warum haben wir eigentlich .net 9 verwendet? .net 10 ist doch viel aktueller!?