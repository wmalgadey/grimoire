# Research: Development Container (devcontainer) Setup

**Feature**: `016-devcontainer-setup` | **Date**: 2026-08-02

## R1: Host container-runtime reachability (Testcontainers, Aspire Dashboard)

- **Decision**: Use the `ghcr.io/devcontainers/features/docker-outside-of-docker`
  devcontainer feature, with `devcontainer.json` forwarding `DOCKER_HOST` from the
  host via `remoteEnv: { "DOCKER_HOST": "${localEnv:DOCKER_HOST}" }`.
- **Rationale**: The project's actual local runtime is Podman
  (`.vscode/tasks.json` runs `podman machine start` before any `docker` CLI task),
  clarified in `spec.md` as the primary/tested runtime while staying
  Docker-API-compatible in general. `docker-outside-of-docker` mounts the host's
  runtime socket as a sibling rather than nesting a second runtime inside the
  container, so whichever socket the contributor's `docker` CLI already points at
  (Podman machine or Docker Desktop) keeps working unchanged.
- **Alternatives considered**:
  - *Docker-in-Docker* — rejected: nested virtualization the project doesn't need;
    Testcontainers has no isolation requirement from the host runtime, and DinD
    would bypass the already-configured Podman machine entirely.
  - *Hardcoded `/var/run/docker.sock` bind mount only* — rejected: silently assumes
    Docker Desktop, contradicting the spec's clarified runtime-agnostic /
    Podman-primary requirement.
- **Recorded in**: `docs/adr/ADR-019-devcontainer-host-runtime-and-credential-access.md`

## R2: Credential delivery (`ANTHROPIC_AUTH_TOKEN` and related `.env-example` vars)

- **Decision**: No new mechanism. The devcontainer's workspace mount already
  includes the full repo checkout, so `<base>/data/.env` — the exact path ADR-009
  already designates for local secrets — is reachable inside the container at the
  same relative location the Hub process reads today.
- **Rationale**: ADR-004 scopes credentials to the specific process that needs them
  and forbids embedding them in build/image layers; ADR-009 fixes `<base>/data/.env`
  as the one place secrets live. Inventing a second credential surface (e.g. a
  `.devcontainer`-local env file, or baking values into `containerEnv`) would
  duplicate and potentially diverge from that existing contract.
- **Complementary mechanism — `devcontainer.json` `secrets` property**: The Dev
  Container Specification defines a `secrets` property that is metadata-only — it
  declares which secret *names* a container expects (e.g. `ANTHROPIC_AUTH_TOKEN`)
  without storing values in `devcontainer.json`. Supporting tools (VS Code Dev
  Containers extension, GitHub Codespaces) can use this declaration to prompt the
  contributor for a value or wire it from a secret store, applying it similarly to
  `remoteEnv`. `devcontainer.json` declares the known `.env-example` variable names
  via `secrets` so tools that support it get a guided experience; `data/.env`
  (mounted with the workspace, per ADR-009) remains the actual source of truth every
  tool falls back to, so contributors on tooling without `secrets` support are
  unaffected. This is additive, not a replacement for the `data/.env` mount.
- **Alternatives considered**:
  - *Bake a `.devcontainer/.env` the contributor edits post-build* — rejected:
    creates a second, easy-to-forget secrets location instead of reusing
    `data/.env`.
  - *`containerEnv` with literal values* — rejected outright: would commit secrets
    to version control (`devcontainer.json` is tracked).
  - *Rely solely on the `secrets` property, drop the `data/.env` mount* — rejected:
    `secrets` is optional metadata support varies by tool (not all devcontainer CLIs
    implement it), so it cannot be the only mechanism; `data/.env` must remain the
    guaranteed fallback.
- **Recorded in**: `docs/adr/ADR-019-devcontainer-host-runtime-and-credential-access.md`

## R6: Aspire Dashboard container — dedicated devcontainer Feature?

- **Finding**: No pre-built devcontainer Feature exists for the standalone
  `mcr.microsoft.com/dotnet/aspire-dashboard` image (confirmed via web research). The
  project's own `.vscode/tasks.json` already runs it as a plain `docker run`, not
  through any packaged Feature. There is nothing off-the-shelf to adopt in its place.
- **Decision**: No change from R1 — the Aspire Dashboard container keeps running the
  same way it does today (`docker run` after the host runtime is available); the
  devcontainer's `docker-outside-of-docker` forwarding (R1) is what makes that
  `docker run` reachable and correct from inside the devcontainer, exactly as it
  already is from the host today.
- **Alternative considered**: Restructuring `devcontainer.json` around a
  `dockerComposeFile` with the Aspire Dashboard as a permanently-running sidecar
  service — rejected for this feature's scope: it would change the devcontainer from
  a single-image config to a Compose-based one project-wide, a larger structural
  change than this feature's onboarding/build/test goal requires, for a container
  that is already optional, on-demand tooling (started manually via the existing
  VS Code task, not part of the build/test path SC-001–SC-003 verify). Worth
  revisiting if a future feature makes the Aspire Dashboard part of the required
  local workflow rather than opt-in observability tooling.

## R3: Toolchain versions to pin in the devcontainer image

- **Decision**: .NET SDK `10.0.x` (matches `backend/Directory.Build.props`
  `<TargetFramework>net10.0</TargetFramework>` and CI's
  `actions/setup-dotnet@v4` `dotnet-version: 10.0.x`); Node `22` (matches
  `frontend/.nvmrc` and CI's `actions/setup-node@v4` `node-version: 22`); Bun
  `1.3.14` (matches `frontend/package.json`'s `packageManager` field and CI's
  `oven-sh/setup-bun@v2` `bun-version: 1.3.14`).
- **Rationale**: Matching CI's pinned versions exactly means "works in the
  devcontainer" and "works in CI" stay the same claim — avoids a class of
  works-locally-fails-in-CI drift the devcontainer is supposed to prevent, not add.
- **Alternatives considered**: Tracking floating major-version tags (e.g. "latest
  .NET 10", "latest Bun") — rejected: reintroduces exactly the version-drift problem
  a pinned devcontainer exists to eliminate; CI pins are the project's own source of
  truth for "the version that must work."

## R4: Base image and provisioning approach

- **Decision**: A custom `.devcontainer/Dockerfile` built `FROM
  mcr.microsoft.com/devcontainers/dotnet:1-10.0`, adding Node 22 and Bun 1.3.14 via
  the corresponding `devcontainers/features` (`ghcr.io/devcontainers/features/node`
  pinned to `22`, plus a `postCreateCommand` installing Bun `1.3.14` — the official
  Bun feature does not yet offer a stable pinned-version guarantee matching
  `package.json`'s `packageManager` field, so pinning via install script keeps the
  version exactly in sync with what `bun.lock`/CI expect).
- **Rationale**: Starting from the official .NET devcontainer base image gets SDK
  installation, common CLI tooling, and a non-root `vscode` user for free; layering
  Node/Bun via devcontainer Features keeps the Dockerfile declarative and lets
  Dependabot-style feature-version bumps stay a one-line change instead of editing
  install scripts by hand, except where a Feature's version pinning is not precise
  enough (Bun), where a scripted install replaces it.
- **Alternatives considered**: Starting `FROM` a bare Ubuntu/Debian image and
  installing everything manually — rejected: reinvents what the official
  `devcontainers/dotnet` image + Features already provide, increasing maintenance
  surface for no benefit.

## R5: CI verification of the devcontainer itself

- **Decision**: Add a CI job using the `devcontainers/ci` GitHub Action to build
  `.devcontainer/` and run the backend build/test and frontend
  check/lint/test/build commands inside the built container, as its own workflow
  or job alongside the existing `.github/workflows/ci.yml` jobs.
- **Rationale**: Without this, the devcontainer config could silently rot (a
  contributor updates `Directory.Build.props`'s target framework or bumps
  `bun.lock`'s Bun version and forgets the devcontainer) with nothing in CI to
  catch it — directly the edge case the spec calls out ("devcontainer environment
  must be updated to match rather than silently drifting out of sync"). It is also
  the mechanism ADR-019 uses as this feature's Constitution III structural-check
  equivalent (Red/Green probe via a deliberately broken devcontainer config).
- **Alternatives considered**: Relying on manual periodic testing — rejected: not
  deterministic, not CI-enforced, violates Constitution IV ("conventions not
  enforced by CI/CD do not exist").
