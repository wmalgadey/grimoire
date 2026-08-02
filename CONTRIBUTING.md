# Contributing to Grimoire

## Prerequisites

- .NET SDK matching `backend/Directory.Build.props`
- Node.js + [Bun](https://bun.sh) (frontend uses `bun.lock`)
- Copy `.env-example` to `data/.env` and fill in the credentials you need locally
  (`ANTHROPIC_AUTH_TOKEN` for agent runs; NVIDIA/LiteLLM vars only if you run evals
  against the affordable provider — see `specs/007-eval-tests-nim-endpoint/quickstart.md`)

## Using the devcontainer (recommended)

This is an additional, opt-in onboarding path — the native setup above remains fully
supported and unaffected.

Prerequisites: a container runtime running on the host (Docker Desktop or Podman — see
`.vscode/tasks.json` for the `podman machine start` step) and a devcontainer-capable
editor (e.g. VS Code with the [Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers)).

1. Open the repository in your editor.
2. Run "Reopen in Container" (or your tool's equivalent).
3. Wait for the container to build — you land in a shell with `dotnet`, `node`, and
   `bun` already installed and on the `PATH`.

See `specs/016-devcontainer-setup/quickstart.md` for a full step-by-step validation
walkthrough (build, test suites, credentials).

Local credentials still go through the same `data/.env` file (copied from
`.env-example`) as the native setup — the devcontainer doesn't change that; it just
makes `data/.env` reachable from inside the container.

**Known limitation**: the `prod` launch configuration in `.vscode/launch.json`
hardcodes a personal, host-absolute `--content-root` path and is not reachable from
inside the devcontainer. This is an intentional, permanent host-only limitation, not a
bug — use `dev`/`proxy` inside the devcontainer, or run `prod` natively on the host.

## Building and testing

```bash
# Backend
dotnet build backend/Grimoire.slnx --configuration Release
dotnet test backend/tests/Grimoire.ArchTests --configuration Release
dotnet test backend/tests/Grimoire.Domain.UnitTests --configuration Release
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release

# Frontend
cd frontend
bun install
bun run check   # type-check
bun run lint
bun run test    # vitest
```

Agent-behavior (evaluation) tests that call a real LLM provider are gated behind
`GRIMOIRE_EVAL=1` and are not part of the default hermetic test run — see
`backend/tests/Grimoire.AgentEvals/`.

## The development process: Spec-Driven Development

All feature work MUST go through the **Spec Kit** workflow — do not implement features
ad hoc. The mandatory order, gated by the project constitution, is:

1. `/speckit-specify` — capture the feature as user scenarios and requirements
2. `/speckit-clarify` (optional) — resolve ambiguities
3. `/speckit-plan` — generate the technical plan; draft any new ADR a structural change needs
4. **ADR review** — any newly drafted ADR must reach *Accepted* status before tasks are generated
5. `/speckit-tasks` — generate a dependency-ordered task list, starting with a Phase 0 structural boundary test
6. `/speckit-implement` — implement the tasks (Red → Green → Refactor)
7. `/speckit-converge` — validate the Definition of Done

Read [`.specify/memory/constitution.md`](.specify/memory/constitution.md) before opening a
PR — it defines the non-negotiable architectural rules (DDD/hexagonal boundaries, the
agentic-core-vs-deterministic-harness split, observability requirements, testing strategy)
that every change is reviewed against. Architectural decisions live in
[`docs/adr/`](docs/adr/) as MADR records; a structural change without an Accepted ADR
covering it will be rejected in review.

## Document map

Not every markdown file in this repo carries the same authority. Before citing or adding
to a document, check its role in the Document Map in [`CLAUDE.md`](CLAUDE.md) — for
example, `docs/decision-context-overview.md` is background/vision material, not a binding
requirement source; only the constitution and Accepted ADRs are binding.

## Pull requests

- One feature branch per Spec Kit feature (`NNN-feature-name`), matching its `specs/NNN-feature-name/` directory
- CI must pass: architecture tests, integration tests, linting, build
- No unapproved infrastructure — new external dependencies (cloud resources, brokers,
  persistence stores) require an approved ADR first
