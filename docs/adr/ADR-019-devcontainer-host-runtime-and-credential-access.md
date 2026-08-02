---
status: accepted
---

# ADR-019: Devcontainer Host Container-Runtime and Credential Access

## Context and Problem Statement

Feature 016 adds a `containers.dev`-conformant devcontainer so contributors can build
and test Grimoire without installing the .NET SDK, Node.js/Bun, or a container
runtime toolchain on the host (spec `016-devcontainer-setup`). This raises two
cross-cutting questions no existing ADR answers:

1. How does a process running *inside* the devcontainer reach a container runtime on
   the *host*, so that `Grimoire.IntegrationTests` (which already references the
   `Testcontainers` package, `Directory.Packages.props`) and the local Aspire
   Dashboard container (ADR-005) keep working from inside the devcontainer? The
   project's own tooling (`.vscode/tasks.json`, `start: podman machine`) shows the
   contributor's actual local runtime is Podman, not Docker Desktop — clarified in
   spec `016-devcontainer-setup` (2026-08-02 session) as: target an OCI-compliant,
   Docker-API-compatible runtime, with Podman as the primary/tested default.
2. How do local credentials (`ANTHROPIC_AUTH_TOKEN` and friends, currently placed at
   `<base>/data/.env` per ADR-009) reach a process inside the devcontainer, without
   being baked into the devcontainer image or committed to the repository?

Both questions are a new *delivery context* for decisions already made elsewhere
(ADR-004's credential-scoping pattern, ADR-009's single path-composition point) — the
devcontainer must honor them, not redefine them — but neither existing ADR covers a
host-to-container boundary, since neither anticipated a devcontainer. Per the
constitution's ADR governance ("if plan.md introduces … an integration pattern … not
covered by existing ADRs, the agent MUST draft a new ADR"), this decision is fixed
here rather than left to task-level improvisation.

## Decision Drivers

- The host runtime is Podman today (`.vscode/tasks.json`), not Docker Desktop; the
  decision must not hardcode a Docker-Desktop-only socket path (spec clarification).
- ADR-004: credentials must stay scoped to the specific process that needs them and
  must never be embedded in an image layer or committed to the repository.
- ADR-009: `<base>/data/.env` is the one place local secrets already live; the
  devcontainer must make that existing file reachable, not invent a second
  credential-delivery mechanism.
- FR-008 (spec 016): no new persistent cloud infrastructure — this is local
  developer-machine plumbing only.
- Constitution Principle III: a cross-cutting decision like this needs a documented,
  structurally-enforceable rule, not an implicit convention.

## Considered Options

1. **`docker-outside-of-docker` devcontainer feature, honoring an inherited
   `DOCKER_HOST`, plus a workspace-relative bind mount of the existing
   `data/.env`.**
2. Docker-in-Docker (nested runtime *inside* the devcontainer, fully isolated from
   the host runtime).
3. Hardcode the conventional `/var/run/docker.sock` bind mount, assuming Docker
   Desktop.
4. Bake credentials into a `.devcontainer`-local `.env` file the contributor edits
   after the container is built.
5. Rely solely on `devcontainer.json`'s `secrets` property, dropping the `data/.env`
   mount.

## Decision Outcome

Chosen option: **Option 1.**

- **Host runtime reachability.** The devcontainer uses the
  `ghcr.io/devcontainers/features/docker-outside-of-docker` feature so that
  containers started by Testcontainers or the Aspire Dashboard task run as siblings
  on the *host's* runtime rather than nested inside the devcontainer. `devcontainer.json`
  forwards `DOCKER_HOST` from the host via `remoteEnv: { "DOCKER_HOST":
  "${localEnv:DOCKER_HOST}" }` so a Podman-machine socket (when the contributor has
  one configured, matching `.vscode/tasks.json`) is used transparently; when
  `DOCKER_HOST` is unset, the feature falls back to its default
  `/var/run/docker.sock` bind mount, which also works for Docker Desktop and for
  Podman configurations that expose a Docker-compatible socket at that path. No
  single runtime is hardcoded as the only supported one.
- **Credential reachability.** The devcontainer workspace mount already includes the
  full repository checkout, so `<base>/data/.env` (ADR-009's existing path) is
  reachable inside the container at the same relative location the Hub process
  already reads from — no new secrets file, no new environment-variable surface, and
  no change to which process (Hub, spawning Ingest per ADR-004) is allowed to read
  it. `data/.env` stays git-ignored exactly as it is today and remains the guaranteed
  source of truth on every devcontainer-capable tool. `devcontainer.json`
  additionally declares only the credential-shaped `.env-example` variable names
  (`ANTHROPIC_AUTH_TOKEN`, `NVIDIA_API_KEY`, `GRIMOIRE_EVAL_PROVIDER_API_KEY` — not
  config values like `GRIMOIRE_INGEST_MODEL`) via the Dev Container Specification's
  metadata-only `secrets` property, so tools that support it (currently GitHub
  Codespaces; VS Code Dev Containers and the bare CLI silently ignore it) can prompt
  the contributor or wire a secret store — this is additive convenience, not a
  replacement for `data/.env`. Third-party secret-manager Features (Infisical,
  1Password) were considered and rejected: both require a real external
  account/server, which is the "unapproved infrastructure" FR-008 and the
  constitution rule out here. The devcontainer configuration files
  (`devcontainer.json`, `Dockerfile`) MUST NOT
  contain literal credential values, and MUST NOT declare `containerEnv` entries
  that hardcode secret values (only non-secret defaults, e.g. tool version pins, are
  permitted there).
- **Scope.** This ADR governs only how the devcontainer reaches the host runtime and
  the existing secrets file. It does not change ADR-004's credential-scoping
  boundary, ADR-009's path-resolution contract, or ADR-010's port/adapter rules —
  Testcontainers and the Aspire Dashboard container remain existing, port-exempt
  infrastructure dependencies of the test/observability setup, not a new external
  system requiring a port.

### Consequences

- Good, because the devcontainer works against the contributor's actual local
  runtime (Podman) without special-casing it, while remaining compatible with Docker
  Desktop hosts through the same feature's default fallback.
- Good, because no new credential-delivery mechanism is introduced — `data/.env`
  keeps meaning exactly what ADR-009 already says it means.
- Bad, because `docker-outside-of-docker` depends on the host socket being
  reachable from inside the devcontainer's mount namespace; a contributor whose
  Podman machine is not running will see the same failure Testcontainers would
  already surface natively (mitigated by documenting the existing `start: podman
  machine` step as a prerequisite in the devcontainer quickstart).
- Neutral: Docker-in-Docker (Option 2) was rejected as unnecessary nested
  virtualization overhead — Testcontainers has no requirement to be isolated from
  the host runtime, and DinD would make the already-configured Podman machine
  irrelevant to the devcontainer.
- Consequential change to an existing file: `.vscode/tasks.json`'s
  `start: podman machine` task manages a *host-level* VM and cannot meaningfully run
  from inside the devcontainer (the `podman` binary manages the machine the
  devcontainer itself already depends on being up before it can build). Its
  `dependsOn` chain (`start: aspire-dashboard` → `start: podman machine`) is guarded
  to no-op when `$REMOTE_CONTAINERS`/`$CODESPACES` is set, so the task remains
  correct both on the host and inside the devcontainer (see `research.md` R7).

## Structural Enforcement (Constitution III)

This feature introduces no C# production code and touches no domain, application, or
adapter namespace, so no `NetArchTest`/ArchUnit-style import rule applies (verified
against ADR-010's scope in `/speckit-plan` research). The Red/Green-probe pattern is
instead realized as a CI smoke check, positioned as the first task in `tasks.md`:

1. **Red**: a CI job builds `.devcontainer/` and runs the backend/frontend build and
   hermetic test commands inside the built container; the job is first written
   against a deliberately incomplete devcontainer config (e.g. missing the .NET
   feature) to confirm the check actually fails when the environment is broken.
2. **Green**: the devcontainer config is completed; the same CI job passes.
3. The job runs in the standard PR pipeline going forward, giving this feature the
   same "guard is live, proven by a controlled failure" property Principle III
   requires of structural boundary tests, applied to configuration rather than code.
