# Quickstart: Development Container (devcontainer) Setup

**Feature**: `016-devcontainer-setup` | **Validates**: spec.md SC-001 – SC-004

This guide proves the devcontainer works end-to-end. It assumes a container runtime is
already available on the host (Podman machine started, or Docker Desktop running) and
a devcontainer-capable editor (e.g. VS Code with the Dev Containers extension) is
installed. Installing those two things is the only host-level prerequisite this
feature does not remove — see spec.md Assumptions.

## 1. Open the repository in the devcontainer (validates SC-001)

1. Start timing.
2. Clone the repository (or open an existing clean checkout).
3. Open it in a devcontainer-capable editor and choose "Reopen in Container" (or the
   equivalent command for your tool).
4. Wait for the container build/provisioning to finish and a shell to open.
5. Stop timing — this should complete in under 15 minutes on a reasonable connection.

**Expected outcome**: A shell inside the container with `dotnet`, `node`, and `bun` on
the `PATH`, at the versions pinned by the repo:

```bash
dotnet --version   # 10.0.x
node --version     # v22.x
bun --version      # 1.3.14
```

## 2. Build backend and frontend without host installation (validates SC-001, SC-002)

From inside the devcontainer shell:

```bash
dotnet build backend/Grimoire.slnx --configuration Release

cd frontend
bun install
bun run check
bun run lint
cd ..
```

**Expected outcome**: Both complete successfully. None of this requires anything
installed on the host beyond the container runtime and the editor.

## 3. Run the hermetic test suites (validates SC-002, SC-003)

```bash
dotnet test backend/tests/Grimoire.ArchTests --configuration Release
dotnet test backend/tests/Grimoire.Domain.UnitTests --configuration Release
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release

cd frontend
bun run test
cd ..
```

**Expected outcome**: All suites pass with the same outcome as running the equivalent
commands via the native host setup in `CONTRIBUTING.md`. If `Grimoire.IntegrationTests`
grows Testcontainers-backed tests, they reach the host's container runtime through the
devcontainer's `docker-outside-of-docker` forwarding (ADR-019) — no extra setup step
inside the container is required beyond the host already having its runtime running.

## 4. Supply local credentials for agent runs (validates SC-004)

Local credentials are not baked into the devcontainer image. On the host (before or
after opening the devcontainer), copy `.env-example` to `data/.env` and fill in the
values you need (see `.env-example` for the full variable list, e.g.
`ANTHROPIC_AUTH_TOKEN`):

```bash
cp .env-example data/.env
# edit data/.env with your local values
```

Because the devcontainer mounts the full repository checkout, `data/.env` is reachable
inside the container at the same path the Hub process already reads it from (ADR-009)
— no separate in-container secrets file exists. `devcontainer.json` also declares the
known variable names via the Dev Container Specification's `secrets` property; on
tools that support it (e.g. VS Code Dev Containers, GitHub Codespaces) you may be
prompted for values directly instead of hand-editing `data/.env`.

**Expected outcome**: Inspecting `.devcontainer/devcontainer.json` and
`.devcontainer/Dockerfile` shows no credential values anywhere in either file — only
non-secret tool-version pins.

## 5. Fall back to the native setup (validates the "tooling unavailable" edge case)

A contributor without a devcontainer-capable editor, or without a container runtime on
the host, can ignore `.devcontainer/` entirely and follow the native setup steps in
`CONTRIBUTING.md` — they are unaffected by this feature.
