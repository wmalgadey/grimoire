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

