---
status: proposed
---

# ADR-027: Container Image and Self-Hosted Deployment Topology

## Context and Problem Statement

Grimoire has no deployment artifact. The only container in the repository is the
devcontainer (ADR-019), which is a *development environment* — a place to build and test
from — not a way to run the product. Running Grimoire today means installing the .NET 10
SDK and Bun on the host, running `dotnet build backend/Grimoire.slnx` so the agent
directory is populated (ADR-022), starting the Hub, and starting `vite dev` beside it.

That last step is load-bearing rather than incidental. `HubEndpoints` maps `/api/*` and
four SignalR hubs under `/hubs/*`, and registers **no static-file middleware and no CORS
policy**. The frontend addresses both with relative paths (`HUB_PATH = '/hubs/…'`,
`fetch('/api/…')`), so the only thing that has ever made the two same-origin is the Vite
proxy configured in `frontend/vite.config.ts` for `dev` and `preview`. Outside a Vite
process, nothing serves the frontend and nothing bridges the two origins. Compounding
this, the frontend is configured with `@sveltejs/adapter-auto`, which fails the build on
any platform it does not recognize — so there is currently no adapter under which a
container build could produce a frontend at all.

This is the gap that makes the product unrunnable by anyone who is not set up to develop
it, and it is why the hi-fi frontend (feature 025) cannot be exercised by a reviewer.

Four questions have no answer in any existing ADR, and each one has a wrong answer that
would silently break a decision already made:

1. **What serves the frontend, and what makes it same-origin with the Hub?** The wrong
   answer adds CORS and a second origin, or moves static hosting into the Hub.
2. **How does the Anthropic credential reach the Hub?** The obvious container answer —
   an environment variable on the Hub service — is precisely what ADR-004 forbids.
3. **Where does the agent runtime come from?** ADR-022 fixed that the hub consumes build
   artifacts and never produces them, and that the agent directory *is* the agent runtime.
4. **Where does mutable state live?** The naive volume layout collides with ADR-022's
   path defaults.

## Decision Drivers

- **ADR-022** is the tightest constraint. The hub must never build; the agent directory
  holds binaries *and* instructions, delivered by each agent's own build via
  `-p:GrimoireAgentDir=…`; `appsettings.json` is the sole source of default paths; the
  four roots (`Data`, `Wiki`, `Agent`, `Memory`) and the secrets file all anchor at the
  **process working directory**; per-option precedence is CLI > `Grimoire__Paths__*`
  environment > configuration file.
- **ADR-022 / `GrimoirePathResolver`**: `agent_dir` and `secrets_file` are
  `RequiredInput` locations — the Hub fails fast at startup when either is absent. A
  deployment that does not deliver both does not start, by design.
- **ADR-004**: the credential is read from a file by the Hub and injected *only* into the
  spawned agent's environment (`AgentProcessHost` explicitly removes and re-sets
  `ANTHROPIC_AUTH_TOKEN` per spawn). It must never enter the Hub's own process
  environment.
- **ADR-005**: the OTLP exporter is registered unconditionally, and the Aspire Dashboard
  is the already-decided local OTLP backend — reusing it introduces no new infrastructure
  decision under Principle IV.
- **markitdown** is a real external subprocess dependency of the ingest convert step
  (`MarkItDownOptions.DefaultExecutablePath = "markitdown"`), installed the same way in
  CI and in the devcontainer. A runtime image without it degrades ingest at runtime, not
  at startup.
- **Constitution Principle IV**: no unapproved infrastructure, and every rule needs a CI
  gate.
- **Constitution Principle II**: this must not change how the test suite runs. No
  containerized dependency enters the suite; the stack introduced here is a *delivery*
  artifact, verified by its own smoke gate.
- **Scope discipline**: this ADR covers a self-hosted, single-host deployment for
  evaluation and internal use. TLS termination, authentication, multi-tenancy, horizontal
  scaling, and a hosted OTel backend are explicitly out of scope and deferred.

## Considered Options

### Frontend hosting and the same-origin problem

- **F1: A reverse proxy in front of both — static SPA build served by the proxy, `/api`
  and `/hubs` proxied to the Hub.** One origin, no CORS, WebSocket upgrade handled by the
  proxy. Requires choosing a concrete SvelteKit adapter (`adapter-static`) and turning off
  SSR.
- **F2: `adapter-node` behind the same reverse proxy.** Keeps SSR available and needs no
  application change beyond the adapter swap, at the cost of a third long-running process
  (Node) in the stack.
- **F3: The Hub serves the built frontend** (`UseStaticFiles` + `MapFallbackToFile`).
  Fewest containers, but it puts frontend hosting inside the Hub — a backend code change
  to `Grimoire.Hub` in service of a packaging concern.
- **F4: Two origins plus a CORS policy on the Hub.** Also a backend change, and it makes
  the Hub's browser-facing surface depend on where the frontend happens to be deployed.

### Credential delivery

- **C1: Mount the operator's existing git-ignored `.env` read-only at the Hub's working
  directory.** No new credential mechanism; the file the Hub already reads, in the place
  it already looks.
- **C2: Set `ANTHROPIC_AUTH_TOKEN` as an environment variable on the Hub service.**
- **C3: A Compose `secret` mounted at `/run/secrets/…`, with
  `Grimoire__Paths__SecretsFile` repointed at it.**

### Agent runtime delivery

- **A1: Build the agent runtimes into the image**, redirecting the build's own
  distribution target with `-p:GrimoireAgentDir=…` — the mechanism ADR-022 designed for
  exactly this.
- **A2: Mount an agent directory built on the host.** Keeps the image smaller.

### Mutable state layout

- **S1: Named volumes at dedicated paths, with the three writable roots relocated there
  via `Grimoire__Paths__*` environment variables.**
- **S2: Named volumes mounted over the default in-image locations** (`/app/.grimoire`,
  `/app/llm-wiki`, `/app/memory`).

## Decision Outcome

**Chosen: F1 + C1 + A1 + S1.**

### The stack is three services behind one published port

| Service | Image | Role |
| --- | --- | --- |
| `proxy` | `caddy` | The only published port. Serves the built frontend; reverse-proxies `/api/*` and `/hubs/*` to `hub`. |
| `hub` | built from `deploy/Dockerfile` | The Hub and the three agent runtimes. Not published to the host. |
| `dashboard` | `mcr.microsoft.com/dotnet/aspire-dashboard` | ADR-005's OTLP receiver and viewer. |

Everything the browser talks to arrives on one origin, so the frontend's relative `/api`
and `/hubs` paths work unchanged and no CORS policy is needed — the proxy occupies exactly
the role Vite's proxy plays in development, which is what keeps the deployed topology
honest about the application's existing assumptions rather than adding new ones.

**F3 and F4 were rejected on the same ground**: both solve a packaging problem by changing
`Grimoire.Hub`. The Hub's HTTP surface is `/api` and `/hubs`; teaching it to serve an SPA
or to negotiate cross-origin requests widens a backend contract because of how we chose to
ship it. **F2 was rejected as an unpaid-for process**: every route's `load` is either a
redirect (`board/+page.ts`) or a pass-through of a route parameter
(`tasks/[taskId]/+page.ts`), and every piece of data on every screen is fetched in the
browser via `fetch` or SignalR after mount. SSR renders empty shells for this application
today, so a Node server would exist to produce them.

**Consequence for the frontend, stated plainly**: `adapter-auto` becomes
`adapter-static` with `fallback: 'index.html'`, and a root `+layout.ts` declares
`ssr = false` — the app becomes a declared SPA rather than an accidental one. This is a
real architectural narrowing and it belongs in this record, not in a build script.
**Revisit trigger**: the first route that needs a server-side `load` — data fetched before
first paint, a server-only secret, SEO-relevant markup — makes F2 the correct shape, and
switching back is an adapter swap plus deleting the `ssr = false` line.

### The credential arrives as a mounted file, never as an environment variable (C1)

The operator's existing `.env` — the same git-ignored file `.env-example` is copied to,
at the same repository-root location ADR-022 anchored it — is bind-mounted read-only into
the Hub's working directory. The Hub reads it with `LocalSecretsLoader` and injects the
token only into each spawned agent's environment, exactly as ADR-004 requires and
unchanged by this ADR.

**C2 is rejected, and it is the trap this section exists to close.** Setting
`ANTHROPIC_AUTH_TOKEN` on the Hub service is the reflexive container idiom, it would
appear to work, and it would put the credential in the Hub's own process environment —
inherited by every child process the Hub spawns, including any future agent with no
business holding it. That is the exact failure ADR-004 chose Option 1 to prevent. The
Compose file therefore declares no credential-shaped variable on any service, and the
image contains no `.env` (`.dockerignore` excludes `.env*`).

**C3 is rejected as indirection without benefit**: a Compose `secret` is itself a
read-only bind mount, so it delivers the same file by a longer path while adding a
configuration override that must be kept in sync with the mount.

### The image carries the agent runtimes, built by the agents' own builds (A1)

The build stage runs `dotnet build … -p:GrimoireAgentDir=/app/.grimoire/agents/`, which is
the redirect ADR-022 designed and documented. Each agent's `PublishAgentRuntime` target
copies its complete output — worker DLL, `deps.json`, `runtimeconfig.json`, every
dependency assembly, and its `Instructions/` — into its own subfolder. The Hub's own
publish output lands beside it in `/app`. The runtime image is
`mcr.microsoft.com/dotnet/aspnet`, whose `dotnet` muxer is what launches
`dotnet /app/.grimoire/agents/<id>/Grimoire.<Type>Agent.dll`.

`markitdown` is installed into the runtime image the same way CI and the devcontainer
install it, so the ingest convert step has the external converter it dispatches to.

**A2 is rejected** because it reintroduces on the host the toolchain requirement this ADR
exists to remove, and it lets a stale host build silently define the deployed agent
behavior. Under A1 the agent runtime and the Hub that spawns it are the same immutable
artifact, which is what ADR-022's "the hub consumes build artifacts" means once there is a
deployment.

### Mutable state lives on named volumes at explicit paths (S1)

`Grimoire__Paths__Data__Dir`, `Grimoire__Paths__Wiki__Dir`, and
`Grimoire__Paths__Memory__Dir` are set to `/var/lib/grimoire/{data,wiki,memory}`, each a
named volume. `Grimoire__Paths__Agent__Dir` is *not* overridden: the agent directory stays
at its in-image location and is deliberately not a volume, because it is a build artifact.

**S2 is rejected because it does not work.** `DataDir` defaults to `.grimoire` and the
agent directory defaults to `.grimoire/agents` — a volume mounted at `/app/.grimoire`
would shadow the agent runtime the image just built, and the Hub would fail its
`agent_dir` validation on first start. Naming the writable roots explicitly keeps the
mutable state and the immutable artifact in separate trees, which is the property that
makes the image disposable and the volumes portable.

Using environment variables for paths is consistent with ADR-022's precedence chain
(`Grimoire__Paths__*` sits between the CLI and `appsettings.json`) and leaves
`appsettings.json` untouched as the sole source of *defaults*.

### Structural Enforcement (Constitution III)

**This ADR introduces no Boundary Rule.** It adds no code, no namespace, and no dependency
direction — nothing in it can be expressed as "package X must not import package Y", and a
reflection or IL-level test would have nothing to inspect. Per Principle III the Phase 0
slot is therefore explicitly empty rather than silently omitted.

The rules below are **Feature-Scoped Invariants**. Following the precedent ADR-019 set for
a configuration-shaped decision, they are enforced by a CI smoke job that builds the stack
and exercises it — the Red/Green probe realized against configuration rather than code —
and by a literal-value scan of the same shape ADR-019's `no-secrets-in-config` job uses.

| Rule | Statement | Enforcement |
| --- | --- | --- |
| **V1** | The stack builds from a clean checkout and answers through the proxy: the frontend document is served at `/`, and `/api/board` reaches the Hub. | `.github/workflows/deploy-smoke.yml` — builds the images, starts the stack with a throwaway `.env`, asserts both responses. |
| **V2** | No deployment file assigns a literal value to a credential-shaped variable, and no service declares `ANTHROPIC_AUTH_TOKEN` in its environment (ADR-004). | Same workflow, denylist scan over `compose.yaml`, `deploy/**` — mirrors ADR-019's SC-004 job. |
| **V3** | A missing secrets file fails the Hub closed at startup, naming `secrets_file`, rather than starting and failing at first dispatch. | Same workflow — starts the Hub with no `.env` mounted and asserts the named startup failure. |

These are Feature-Scoped Invariants and not Boundary Rules because each protects the
current shape of one delivery surface: the service list, the mount layout, and the
published route set are all expected to change when the deployment itself changes.
Consistent with Principle III, each is verified by exercising the real observable behavior
(an HTTP response, a startup failure) rather than by asserting the contents of a YAML
file — with V2 as the deliberate exception, where the literal *is* the hazard and the
scan is the same narrow, credential-only denylist ADR-019 already justified.

### Consequences

- **Good**: Grimoire becomes runnable by someone who has only a container runtime —
  `docker compose up` against a checkout and a `.env`. The .NET SDK, Bun, and the
  build-the-agents-first step disappear from the operator's path.
- **Good**: the deployed topology tells the truth about the application. The proxy is the
  same same-origin bridge Vite provides in development, so the browser-facing contract is
  identical in both, and no new backend surface (static hosting, CORS) is invented to
  paper over a packaging gap.
- **Good**: ADR-004's credential boundary survives containerization intact, and V2 makes
  the tempting violation a build failure rather than a review catch.
- **Good**: ADR-022's "the hub consumes build artifacts, never produces them" gains its
  natural deployment expression — the agent runtime is baked into the same immutable
  artifact as the Hub that spawns it.
- **Bad / accepted**: the frontend is now a declared SPA. Server-side rendering is off
  everywhere, including `bun run dev`, so a future route needing a server `load` requires
  the F2 swap recorded above. Given that no route uses one today, this makes an existing
  reality explicit rather than removing a capability in use.
- **Bad / accepted**: the image is large. It carries the ASP.NET runtime, a Python
  interpreter with markitdown, and three agent runtimes that each hold their own copy of
  the shared dependency set — the per-agent duplication ADR-022 already accepted and
  justified, now multiplied into an image layer.
- **Good**: both containers are rootless by default — a numeric non-root uid (so
  Kubernetes' `runAsNonRoot` can verify it), all capabilities dropped, `no-new-privileges`
  set, and writable roots owned by group 0 and group-writable so an arbitrary `runAsUser`
  can write to them without an init container. Nothing under `/app` is written at runtime.
- **Bad / accepted, and stated rather than implied**: **this stack is still not hardened
  for exposure to an untrusted network.** It has no TLS, no authentication in front of the
  Hub or the UI, and an unsecured telemetry dashboard. Those are properties of this compose
  stack rather than of the image — a cluster deployment would front it with a TLS ingress
  and an auth proxy. Publishing it beyond a trusted host is a decision this ADR does not
  make.
- **Bad**: a fourth long-running artifact (the proxy configuration) now encodes the route
  split between frontend and Hub. Adding a route prefix to `HubEndpoints` without adding
  it to the proxy configuration produces a 404 that the test suite cannot see; V1 covers
  the two prefixes that exist today, not future ones.
- **Bad / accepted**: the smoke workflow is path-filtered rather than running on every
  pull request. A full image build duplicates work `ci.yml`'s backend and frontend jobs
  already do, so it triggers on the files that define the deployment or that its build
  contract depends on, plus `workflow_dispatch`. The gap this leaves is a backend change
  that breaks only the image build; it surfaces on the next deployment-touching pull
  request or a manual run, not immediately.
- **Neutral**: the Aspire Dashboard is reused per ADR-005 rather than re-decided, so no
  new infrastructure is introduced under Principle IV. A production OTel backend remains
  the deferred decision ADR-005 already recorded.
- **Neutral**: the test suite is untouched. No test gains a container dependency, and
  Principle II's "real infrastructure, in-process/on-disk/child-process" verification
  model is unaffected.

## More Information

Operator documentation: `deploy/README.md`. The stack definition is `compose.yaml`, the
image is `deploy/Dockerfile`, and the proxy configuration is `deploy/Caddyfile`.

Per the constitution's Spec-Kit workflow (step 4), this ADR must reach **Accepted** status
via explicit project-owner sign-off before the deployment it describes is merged. It is
left `proposed` deliberately: it narrows the frontend's rendering model and adds the first
non-development container to the repository, and both deserve an explicit decision rather
than an author-accepted default.
