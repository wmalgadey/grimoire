## Summary

<!-- One or two sentences: what changed and why. -->

## Type of change

- [ ] SDD feature (implements a `specs/NNN-feature-name/` Spec Kit feature)
- [ ] Bug fix
- [ ] Housekeeping / cleanup
- [ ] ADR only (architecture decision record, no code)
- [ ] Documentation
- [ ] CI / tooling
- [ ] Other:

## Related

- Spec: `specs/NNN-feature-name/` <!-- if applicable -->
- Issue(s): closes #
- ADR(s) touched:

## Spec-Driven Development checklist

<!--
  Skip this whole section for pure housekeeping/docs/CI PRs that touch nothing under
  `specs/` or `docs/adr/`. Required for anything implementing or amending a Spec Kit
  feature — see CONTRIBUTING.md "The development process" and the project constitution's
  Definition of Done.
-->

- [ ] Followed the Spec Kit workflow (`/speckit-specify` → `/speckit-clarify` → `/speckit-plan` → ADR review → `/speckit-tasks` → `/speckit-implement` → `/speckit-converge`) — no ad hoc feature work
- [ ] `plan.md` lists every ADR constraining this implementation, and each is in `docs/adr/` with `status: accepted`
- [ ] Any new structural boundary / cross-cutting concern this PR introduces has an Accepted ADR covering it (Principle III); if it supersedes or amends another ADR, both status headers link to each other and `docs/adr/index.md` is updated
- [ ] Phase 0 structural boundary tests exist for every Boundary Rule in `plan.md`, verified Red→Green
- [ ] Feature-Scoped Invariants are covered by classicist, state-based integration tests — not reflection/IL-based structural tests
- [ ] `plan.md ## Observability` (metrics, structured log events, trace spans) is fully implemented and tested against the real telemetry composition root, with a final-phase completeness-audit task confirming it
- [ ] Agent-judgment success criteria (if any) are covered by evaluation-style tests at the spec's defined thresholds, never by a 100% hermetic assertion
- [ ] No wiki-content judgment was implemented as deterministic backend code (Principle V) — behavior changes live in agent instruction files, not C#/TS logic

## Testing

- [ ] `./scripts/test-fast.sh` passes
- [ ] `Grimoire.IntegrationTests` passes (required if this touches `Grimoire.Hub` or agent dispatch)
- [ ] `Grimoire.AgentEvals` SlowEval tier passes (required if this changes agent instructions, prompts, or eval scenarios)
- [ ] Frontend checks pass — `bun run check && bun run lint && bun run test` (if `frontend/` touched)
- [ ] Manually exercised the change (UI and/or CLI) where applicable

## Architecture & infrastructure

- [ ] No new external dependency (cloud resource, broker, persistence store) was introduced without an Accepted ADR
- [ ] `Grimoire.Domain` still imports nothing from Infrastructure/Framework/Adapter packages
- [ ] All code, comments, and documentation added or changed are in English (per `CLAUDE.md`)
