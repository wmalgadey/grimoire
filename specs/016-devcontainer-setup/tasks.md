# Tasks: Development Container (devcontainer) Setup

**Input**: Design documents from `/specs/016-devcontainer-setup/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, quickstart.md

**Tests**: This feature is developer tooling/configuration, not application code — there is
no unit/integration test framework to add. Its "tests" are the CI smoke job
(`devcontainer-ci.yml`, itself the Constitution III structural-enforcement equivalent per
ADR-019) and the `quickstart.md` validation runs, both woven into the phases below rather
than kept as a separate section.

**Observability**: plan.md's `## Observability` section is N/A with justification (no
runtime code path is introduced). No log/trace/metric implementation or contract tasks
apply; the Polish phase still carries a completeness-audit task confirming that N/A
determination, per Constitution III/IV.

**Organization**: Tasks are grouped by user story (spec.md) to enable independent
implementation and validation of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Prove the CI check that guards this feature can actually fail, before the
real devcontainer is built out. Realizes ADR-019's Red/Green-probe equivalent (no C# code
boundary exists here — see ADR-019 § Structural Enforcement).

**⚠️ NON-NEGOTIABLE**: No further devcontainer work begins until the Red probe below is verified.

- [ ] T000 Create `.github/workflows/devcontainer-ci.yml` using the `devcontainers/ci`
      GitHub Action pointed at `.devcontainer/`, and create a **deliberately incomplete**
      `.devcontainer/devcontainer.json` + `.devcontainer/Dockerfile` (`FROM
      mcr.microsoft.com/devcontainers/dotnet:1-10.0` only — no Node/Bun feature yet).
      Locally run `devcontainer build --workspace-folder .` followed by `devcontainer exec
      --workspace-folder . -- bash -lc 'node --version'` and confirm it fails (Node
      missing) — this is the Red state, proving the check would catch a broken/incomplete
      toolchain. Leave the incomplete scaffold in place; Phase 2 completes it.

**Red probe result**: _(record the actual failing output here once run)_

**Checkpoint**: CI guard proven live. Feature work may now begin.

---

## Phase 1: Setup

**Purpose**: Confirm the scaffold Phase 0 created is the correct starting point — no
additional initialization needed for a feature this size.

- [ ] T001 Confirm `.devcontainer/` directory (created by T000) is the sole new top-level
      structure and matches plan.md's Project Structure section; no build tool, package
      manager, or additional scaffolding to initialize beyond what T000 already created.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared toolchain every user story runs on top of. Completes T000's Red
scaffold into a working base image.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T002 Complete `.devcontainer/Dockerfile`: `FROM
      mcr.microsoft.com/devcontainers/dotnet:1-10.0`, add Node 22 via the
      `ghcr.io/devcontainers/features/node:1` feature (or an equivalent apt/official
      NodeSource install pinned to `22` if simpler inside a single Dockerfile), and a
      pinned Bun `1.3.14` install step (research.md R3/R4 — Bun's own Feature does not
      pin precisely enough to match `frontend/package.json`'s `packageManager` field).
- [ ] T003 Complete `.devcontainer/devcontainer.json` base config (depends on T002):
      `build.dockerfile` reference, `customizations.vscode.extensions` (C# Dev Kit,
      Svelte for VS Code, ESLint, Prettier), `forwardPorts` (`5255` Hub,
      `5173` frontend dev server, `18888`/`4317` Aspire Dashboard), `postCreateCommand`
      (`dotnet restore backend/Grimoire.slnx && cd frontend && bun install`).
- [ ] T004 Validate the completed devcontainer via `devcontainer build --workspace-folder
      .` and `devcontainer exec --workspace-folder . -- bash -lc 'dotnet --version &&
      node --version && bun --version'`, confirming all three report the pinned versions
      (10.0.x / v22.x / 1.3.14) — this is the **Green** confirmation for Phase 0's probe.

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Onboard without installing toolchains on the host (Priority: P1) 🎯 MVP

**Goal**: A contributor with nothing but a container runtime and a devcontainer-capable
editor gets a working build/test shell without installing .NET, Node, or Bun on the host.

**Independent Test**: Per spec.md — open a clean checkout in the devcontainer and confirm
the backend and frontend build commands complete without any host-installed toolchain.

- [ ] T005 [US1] Run `quickstart.md` steps 1–2 (open in devcontainer; `dotnet build`,
      `bun install && bun run check && bun run lint`) against the Phase 2 devcontainer
      and record actual wall-clock time, confirming SC-001 (<15 minutes).
- [ ] T006 [US1] Update `CONTRIBUTING.md`: add a devcontainer onboarding section
      (Prerequisites: container runtime + devcontainer-capable editor; "Reopen in
      Container"; link to `specs/016-devcontainer-setup/quickstart.md`), keeping the
      existing native host-setup instructions unchanged and intact as a fallback (FR-007).

**Checkpoint**: User Story 1 is fully functional and independently testable — this is the MVP.

---

## Phase 4: User Story 2 - Run the full test suite inside the container (Priority: P2)

**Goal**: The hermetic backend and frontend test suites, including integration tests that
need a container runtime, run from inside the devcontainer without falling back to the host.

**Independent Test**: Per spec.md — from inside the devcontainer, run the backend
architecture/unit/integration test commands and the frontend check/lint/test commands and
confirm they complete with the same outcome as the native setup.

- [ ] T007 [US2] Add the `ghcr.io/devcontainers/features/docker-outside-of-docker:1`
      feature to `.devcontainer/devcontainer.json`, plus `remoteEnv: { "DOCKER_HOST":
      "${localEnv:DOCKER_HOST}" }` (FR-004; ADR-019 / research.md R1). Document the
      Podman-socket prerequisite already noted in `quickstart.md`.
- [ ] T008 [US2] Run `quickstart.md` step 3 (`dotnet test` × 3 projects, `bun run test`)
      inside the devcontainer and confirm the outcome matches the existing native
      `.github/workflows/ci.yml` job (SC-002, SC-003).
- [ ] T009 [P] [US2] Extend `.github/workflows/devcontainer-ci.yml` (from T000) to run the
      same backend test commands (`dotnet test` × 3 projects) and frontend
      `check|lint|test|build` commands inside the built container, so the parity check in
      T008 is CI-enforced going forward, not just a one-time manual run.

**Checkpoint**: User Stories 1 AND 2 both work independently.

---

## Phase 5: User Story 3 - Supply local credentials without baking them into the image (Priority: P3)

**Goal**: A contributor can supply `ANTHROPIC_AUTH_TOKEN` and related credentials to the
devcontainer without them being committed to the repo or embedded in the image.

**Independent Test**: Per spec.md — inspect `.devcontainer/devcontainer.json` and
`Dockerfile` for credential values (must find none), then confirm a process inside the
devcontainer can read a credential supplied via `data/.env`.

- [ ] T010 [P] [US3] Add the declarative `secrets` property to
      `.devcontainer/devcontainer.json` for `ANTHROPIC_AUTH_TOKEN`, `NVIDIA_API_KEY`, and
      `GRIMOIRE_EVAL_PROVIDER_API_KEY` (research.md R2 — metadata-only, no values).
- [ ] T011 [US3] Add an SC-004 static-scan step to `.github/workflows/devcontainer-ci.yml`
      that greps `.devcontainer/devcontainer.json` and `.devcontainer/Dockerfile` for
      literal values of the known `.env-example` credential variable names and fails the
      job if any are found.
- [ ] T012 [US3] Run `quickstart.md` step 4: copy `.env-example` to `data/.env`, confirm a
      process inside the devcontainer (e.g. `cat data/.env` from the devcontainer shell)
      can read it, and confirm inspection of `.devcontainer/devcontainer.json` and
      `Dockerfile` shows zero credential values (SC-004).

**Checkpoint**: All three user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Repo-hygiene fixes the devcontainer surfaced, plus the mandatory
completeness audit.

- [ ] T013 Observability completeness audit (MANDATORY — Constitution Principle III/IV):
      confirm plan.md's `## Observability` N/A determination still holds after
      implementation (no metrics, log events, or trace spans were introduced) — file a
      gap task if anything observable was in fact added.
- [ ] T014 Fix `.vscode/tasks.json`: guard the `start: podman machine` task to no-op when
      `$REMOTE_CONTAINERS`/`$CODESPACES` is set, since it manages a host-level VM that
      cannot run meaningfully from inside the devcontainer (research.md R7).
      `command`: `[ -n "$REMOTE_CONTAINERS$CODESPACES" ] || (podman machine list --format
      '{{.Running}}' | grep -q true || podman machine start)`.
- [ ] T015 [P] Note `.vscode/launch.json`'s `prod` configuration's host-only limitation
      (hardcoded personal `--content-root` path unreachable inside the devcontainer) in
      `CONTRIBUTING.md`'s new devcontainer section — no change to `launch.json` itself
      (research.md R7).
- [ ] T016 Run `quickstart.md` end-to-end in one pass and confirm SC-001–SC-004 all hold
      together, not just individually per story.
- [ ] T017 Confirm `.github/workflows/devcontainer-ci.yml` passes green on a real PR run —
      the final Green confirmation completing ADR-019's Red/Green structural-enforcement
      probe (T000 was Red; T004 was local Green; this is CI Green).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0**: No dependencies — creates the Red probe scaffold.
- **Phase 1**: Depends on Phase 0 (confirms its output).
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories.
- **Phase 3 (US1)**: Depends on Phase 2 only.
- **Phase 4 (US2)**: Depends on Phase 2 only (independent of US1's tasks, though both read
  the same `devcontainer.json` — see file-conflict note below).
- **Phase 5 (US3)**: Depends on Phase 2 only (independent of US1/US2).
- **Phase 6 (Polish)**: Depends on Phases 3–5 all being complete.

### File-conflict note

T003 (Foundational), T007 (US2), and T010 (US3) all edit
`.devcontainer/devcontainer.json`. They are **not** parallel-safe against each other
despite belonging to different phases/stories — T003 must land first (Foundational gate),
then T007 and T010 each add a disjoint top-level key (`features`/`remoteEnv` vs.
`secrets`) and can be applied in either order, but not truly concurrently against the same
file without a merge step. Similarly T009 and T011 both extend
`.github/workflows/devcontainer-ci.yml` — same caveat.

### Parallel Opportunities

- T009 [US2] and T010 [US3] touch disjoint files/keys and can be worked in parallel once
  Phase 2 is complete, provided their edits to shared files (see above) are merged
  sequentially rather than both written blind.
- T006 [US1] (`CONTRIBUTING.md`), T014 (`.vscode/tasks.json`), and T015
  (`CONTRIBUTING.md` addendum) touch different files from the `.devcontainer/*` work and
  can proceed in parallel with Phases 3–5.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 0 (Red probe) → Phase 1 → Phase 2 (Foundational toolchain).
2. Complete Phase 3 (US1): onboarding build/test works, `CONTRIBUTING.md` updated.
3. **STOP and VALIDATE**: run `quickstart.md` steps 1–2 independently — this alone is a
   shippable MVP (a contributor can build without host installs, even before
   Testcontainers-reachability or credential-declaration polish land).

### Incremental Delivery

1. Phase 0–2 → foundation ready.
2. Phase 3 (US1) → demoable MVP.
3. Phase 4 (US2) → test suite fully portable into the container.
4. Phase 5 (US3) → credential story complete.
5. Phase 6 → CI-enforced going forward, repo hygiene (tasks.json) fixed, DoD closed.
