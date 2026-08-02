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
  - *Docker Compose-based devcontainer* (`dockerComposeFile` + a sidecar service) —
    considered specifically for reaching the host runtime, not just for the Aspire
    Dashboard (see R6). Rejected for this concern too: Compose only helps with
    *statically declared* sidecar services; Testcontainers dials the runtime
    directly per test to spin up ad-hoc containers, which a Compose sidecar cannot
    provide — a Compose-based devcontainer would still need `docker-outside-of-docker`
    (or an equivalent socket mount) bolted on for that, so Compose adds a second
    moving part without removing the first.
- **Confirmed against independent research**: Microsoft's own (now-archived, moved to
  aspire.dev) guidance for devcontainer + Aspire/dashboard scenarios recommends
  Docker-outside-of-Docker over Docker-in-Docker for exactly this reason ("DinD
  incurs performance overhead compared to native Docker; consider DooD"), and treats
  Compose as relevant only for external backend dependencies (e.g. Redis/Postgres in
  Dapr scenarios), not for host-runtime reachability itself. This independently
  validates the DooD choice.
- **macOS Podman-socket nuance**: `docker-outside-of-docker` bind-mounts the host's
  `/var/run/docker.sock` by default. On this project's actual host setup (Podman on
  macOS via `podman machine`), that path resolves correctly only if the machine's
  Docker-API-compatible socket is already symlinked/exposed there (e.g. Podman
  Desktop's "Docker Compatibility" setting, or a rootful default machine) — the same
  precondition the existing `.vscode/tasks.json` `docker` CLI calls already rely on
  today. `devcontainer.json` additionally forwards `DOCKER_HOST` from the host via
  `remoteEnv: { "DOCKER_HOST": "${localEnv:DOCKER_HOST}" }` as a fallback/override
  for contributors whose Podman socket lives elsewhere, so the default-path
  assumption is not a hard requirement. Documented as a devcontainer prerequisite in
  `quickstart.md` rather than silently assumed.
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
  Container Specification's declarative `secrets` property is metadata-only —
  `{"NAME": {"description": "..."}}`, no values — and is distinct from the
  `devcontainer` CLI's separate `--secrets-file` flag (also value-free by design;
  `secrets-support.md` states outright secrets don't belong in `devcontainer.json`).
  Today, only GitHub Codespaces actively consumes the declarative property (prompting
  for a "recommended secret" by name at codespace-creation time); the VS Code Dev
  Containers extension and the bare `devcontainer` CLI ignore unknown keys silently —
  additive with zero risk on tools that don't support it. Only the credential-shaped
  `.env-example` variables are declared (config values like `GRIMOIRE_INGEST_MODEL`
  are not secrets and are omitted):

  ```jsonc
  "secrets": {
    "ANTHROPIC_AUTH_TOKEN": { "description": "Anthropic API token for the Ingest agent (data/.env)" },
    "NVIDIA_API_KEY": { "description": "NVIDIA NIM key for eval runs against the LiteLLM proxy (data/.env)" },
    "GRIMOIRE_EVAL_PROVIDER_API_KEY": { "description": "Eval-provider API key, gated behind GRIMOIRE_EVAL=1 (data/.env)" }
  }
  ```

  `data/.env` (mounted with the workspace, per ADR-009) remains the actual source of
  truth every tool falls back to, so contributors on tooling without `secrets`
  support are unaffected. This is additive, not a replacement for the `data/.env`
  mount.
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
  - *`remoteEnv`/`${localEnv:...}` passthrough of the credential values themselves*
    — rejected: would inject secrets into the whole container/VS Code Server process
    environment, duplicating and bypassing the file-scoped loader
    (`LocalSecretsLoader`, `backend/src/Grimoire.Hub/AgentDispatch/Adapters/AgentProcess/`)
    that ADR-004 deliberately scopes to the Ingest child process only, for no
    functional gain.
  - *Third-party secret-manager Features* (e.g. an Infisical or 1Password
    devcontainer Feature) — rejected: both require a real external account/server
    (an Infisical project, a 1Password Connect server), which is exactly the
    "unapproved infrastructure" the constitution and FR-008 rule out for a project
    that already has a working single-file local-secrets contract with one consumer.
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

## R7: `.vscode/tasks.json` / `launch.json` compatibility with the devcontainer

VS Code executes `.vscode/tasks.json` tasks *inside* the active workspace context —
when a contributor is attached to the devcontainer, tasks run inside the container,
not on the host. Auditing the existing tasks/launch configs against that surfaced one
real incompatibility and one pre-existing limitation, neither hypothetical:

- **`start: podman machine` is host-only and must not run inside the container.**
  Today, `start: aspire-dashboard` `dependsOn` `start: podman machine`, whose command
  (`podman machine list ... || podman machine start`) manages a *host-level* VM — an
  operation that is meaningless (and whose `podman` binary is not installed) from
  inside the devcontainer. By the time the devcontainer itself can build, the host
  runtime the devcontainer depends on must already be running, so the task's job is
  already done in that context; running it again inside the container would simply
  fail. **Decision**: guard the task's command to no-op when running inside a
  devcontainer/Codespace, detected via the standard `REMOTE_CONTAINERS` (Dev
  Containers) / `CODESPACES` environment variables VS Code Server sets:
  `command: "[ -n \"$REMOTE_CONTAINERS$CODESPACES\" ] || (podman machine list --format '{{.Running}}' | grep -q true || podman machine start)"`.
  This keeps the task's current host behavior identical for contributors not using
  the devcontainer, while making it a safe no-op inside one. This is a proposed
  change to an existing repo file (`.vscode/tasks.json`), in scope for this feature
  per FR-007's "onboarding path" mandate and the user's explicit request to review
  these files.
- **`start: aspire-dashboard`'s own `docker run` needs no change.** It already
  becomes correct inside the devcontainer once `docker-outside-of-docker` forwards
  the socket (R1) — no edit needed to this task itself.
- **`launch.json`'s `coreclr` debug type works unmodified inside a devcontainer** —
  this is a standard, well-supported VS Code Dev Containers pattern (the C# Dev Kit
  debug adapter runs inside the container alongside VS Code Server); no changes
  needed to the `dev`/`proxy`/`prod` configurations' `type`, `program`, or `args`.
- **Pre-existing limitation, not something this feature should silently fix**: the
  `prod` launch configuration's `--content-root` argument hardcodes a personal,
  host-absolute path (`/Volumes/Daten/parainoid/llm-wiki`) outside the repository
  checkout. This path cannot be reachable inside the devcontainer's filesystem
  (nothing bind-mounts it), and hardcoding a bind mount for one contributor's
  personal directory into the shared, version-controlled `devcontainer.json` would
  leak personal machine layout into the repo for everyone else. **Decision**: flag
  this to the feature's author rather than silently patch it — the `prod` launch
  profile is scoped as host-only / not usable from inside the devcontainer, which is
  a reasonable boundary (testing against a real external content root is arguably
  out of scope for a *contributor onboarding* devcontainer regardless). No task
  generated for this; documented here and in the completion report for a human
  decision.

## R8: Findings from actually building and running the devcontainer

R3/R4's toolchain plan was validated (and corrected) by building the real image and
running every quickstart/CI command inside it, not just by inspecting config files.
Three real, reproducible issues surfaced this way that pure research could not have
caught:

- **Base image ships a stale, unrelated apt source.**
  `mcr.microsoft.com/devcontainers/dotnet:1-10.0` pre-bakes an `/etc/apt/sources.list.d/yarn.list`
  entry (Yarn Classic, which this project doesn't use — it uses Bun) whose keyring no
  longer validates against `dl.yarnpkg.com`'s current signing key. This breaks *every*
  subsequent `apt-get update`, including ones devcontainer Features run internally —
  concretely, it made the `docker-outside-of-docker` feature's own install script
  fail. Fixed with one `Dockerfile` line: `RUN rm -f /etc/apt/sources.list.d/yarn.list`.
- **`markitdown` CLI is a real, undeclared dependency of `Grimoire.IntegrationTests`.**
  `IngestConvertStepTests` exercises the actual `markitdown` conversion path (its own
  comment: "FR-015: default path converts (real markitdown)"). `.github/workflows/ci.yml`
  installs it (`pip install --break-system-packages markitdown`) but neither R3 nor R4
  had it in scope — 3 of 583 IntegrationTests failed without it. Fixed by adding the
  identical install step to the Dockerfile, matching CI exactly (same rationale as R3:
  devcontainer and CI must stay the same claim).
- **`bun run test`'s browser-mode suite needs Playwright's Chromium binary.**
  `.github/workflows/ci.yml` runs `bunx playwright install --with-deps chromium` as a
  separate step; `bun install` alone does not fetch the browser binary
  `@vitest/browser-playwright` launches at test time. Fixed by adding the same command
  to `.devcontainer/post-create.sh`, immediately after `bun install`.

**Verified outcome after both fixes** (run inside the actual built devcontainer, not
inferred): backend `dotnet build` succeeds; `Grimoire.ArchTests` 49/49 pass;
`Grimoire.Domain.UnitTests` 93/93 pass; `Grimoire.IntegrationTests` 583/583 pass;
frontend `bun run check`/`lint` report zero errors; `bun run test` 123/123 tests across
22 files pass; `bun run build` succeeds. `docker ps` run from inside the devcontainer
correctly lists containers on the *host* runtime (confirming R1's
`docker-outside-of-docker` sibling-forwarding actually works, not just in theory).
`data/.env` (copied from `.env-example`) was confirmed reachable at the same relative
path inside the container, matching ADR-009. `.devcontainer/devcontainer.json` and
`Dockerfile` were scanned and contain zero literal credential values.

**Implication for R3/R4**: their toolchain-version decisions were correct; their scope
was incomplete — "the toolchain" for this repo includes two external CLI tools
(`markitdown`, Playwright's Chromium) beyond the SDK/runtime triplet, both already
present in CI but not originally carried over into the devcontainer design. Both are
now part of the Dockerfile/`post-create.sh` (see `.devcontainer/`), not a gap in the
shipped feature.
