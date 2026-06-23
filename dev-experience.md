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
