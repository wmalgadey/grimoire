# Running Grimoire

A self-hosted stack: the Hub, the three agent runtimes, the frontend, and a local
telemetry dashboard.

You need a container runtime with Compose v2. You do **not** need the .NET SDK, Bun, or a
prior `dotnet build` — the image builds and carries the agent runtimes itself.

This is packaging, not architecture: nothing here changes the product, and the harness
still runs without a container the usual way (`dotnet build backend/Grimoire.slnx`, run
the Hub, `bun run dev` beside it). The reasoning behind the choices below lives in this
file rather than in an ADR for that reason — see [Why it is shaped this way](#why-it-is-shaped-this-way).

## Start it

```bash
cp .env-example .env          # then put a real ANTHROPIC_AUTH_TOKEN in it
docker compose up --build     # from the repository root
```

Then open <http://localhost:8080>. The telemetry dashboard is on
<http://localhost:18888>.

Override the published ports with `GRIMOIRE_PORT` and `GRIMOIRE_DASHBOARD_PORT`.

## The `.env` file is the whole configuration surface

It carries the credential *and* every agent setting the Hub passes on — model overrides,
base URLs, the ingest token cap. `LocalSecretsLoader` reads all of them from this one
file, so `.env-example` is the complete list of what you can set.

It is mounted read-only into the Hub, and it is a **required input**: without it the Hub
refuses to start, naming `secrets_file` and the path it looked at, rather than starting
and failing on the first dispatch.

The credential deliberately does **not** travel as a service environment variable. ADR-004
scopes it to the spawned agent process — the Hub reads the file and injects the token per
spawn, so it never enters the Hub's own environment where every child would inherit it.
Adding it to `compose.yaml` would look like it works and would quietly undo that; CI fails
the build if it appears there.

One wrinkle worth knowing: Compose also reads `./.env` for its own variable substitution.
That is harmless — the stack only substitutes the two port variables — but a token
containing a literal `$` will make Compose emit an interpolation warning.

## What runs

| Service | Port | Notes |
| --- | --- | --- |
| `proxy` | 8080 | The only published application port. Serves the frontend and forwards `/api` and `/hubs` to the Hub, so the browser sees one origin. |
| `hub` | — | The Hub and the agent runtimes. Reachable only from inside the stack. |
| `dashboard` | 18888 | Traces, metrics and logs (ADR-005). Holds no credential. |

State lives in three places, managed volumes by default:

- `grimoire-wiki` — **the product**. Back this one up.
- `grimoire-data` — operational-state database and raw sources.
- `grimoire-memory` — task, conversation, finding and remediation records.

The agent directory is *not* a volume. It is a build artifact inside the image, so the Hub
and the agents it spawns are always the same build.

### Pointing the Hub at your own directories

Each of the three takes a host path instead, without editing `compose.yaml`. Compose picks
the kind from the shape of the value — a bare name is a managed volume, anything starting
with `/` or `./` is a bind mount — so setting any of these in `.env` is enough:

```bash
GRIMOIRE_WIKI_DIR=/home/me/obsidian/my-vault   # run against an existing vault
GRIMOIRE_DATA_DIR=./state/data
GRIMOIRE_MEMORY_DIR=./state/memory
```

Mix freely: an unset variable keeps its managed volume while the others are bound to host
paths.

**If you bind-mount a directory you also use from the host — a real Obsidian vault, say —
run the Hub as yourself:**

```bash
GRIMOIRE_UID=1000    # your `id -u`
```

Both containers are rootless by default and run as uid `1654`, so without this the pages,
`log.md` entries and records the Hub writes into your vault belong to `1654` rather than
to you. Point `GRIMOIRE_UID` at your own id and `chown` the directory to yourself once,
and everything the Hub creates is yours.

Leave `GRIMOIRE_GID` alone. The image's writable roots are owned by group 0 and are
group-writable, which is what lets *any* uid write to them — change the group and a
managed volume stops being writable.

One more thing when you set it: `.env` has to be readable by that id, or the Hub fails
closed naming `secrets_file`.

## Security posture

Rootless by default, on terms meant to survive the move to a cluster:

- Both containers run as a **numeric non-root uid** (`1654`). Numeric rather than a
  username because Kubernetes' `runAsNonRoot` admission check can only verify a number.
- Writable directories are owned by **group 0 and group-writable**, the arbitrary-uid
  convention OpenShift requires. A `runAsUser: <anything>` pod can write to them with no
  init container fixing permissions first.
- **All capabilities dropped**, `no-new-privileges` set. Nothing needs a capability: the
  ports are unprivileged, the Hub spawns agents as its own uid, and it writes only under
  `/var/lib/grimoire`.
- Nothing under `/app` is written at runtime — the secrets file and the agent runtime are
  read-only inputs — so the Hub's own image content stays untouched while it runs.

What is still **not** covered, and would be needed before this faces an untrusted network:
there is no TLS, no authentication in front of the Hub or the UI, and the telemetry
dashboard is deliberately unsecured. Those are properties of this compose stack, not of
the image; a cluster deployment would put an ingress with TLS and an auth proxy in front.

## Hub CLI commands

Starting the server is what the Hub does with no command name. Every other command is
reachable in the running container:

```bash
docker compose exec hub dotnet /app/Grimoire.Hub.dll --help
docker compose exec hub dotnet /app/Grimoire.Hub.dll lint run
```

## Updating

```bash
git pull
docker compose up --build -d
```

The volumes survive; the image is disposable.

## Why it is shaped this way

The rationale, kept here because each choice is a consequence of a decision already
recorded elsewhere rather than a new decision of its own.

**A reverse proxy rather than static hosting inside the Hub.** `HubEndpoints` maps `/api/*`
and four SignalR hubs under `/hubs/*`, and registers no static-file middleware and no CORS
policy. The frontend addresses both with relative paths, so in development the *only* thing
making them same-origin is the Vite proxy in `frontend/vite.config.ts`. Caddy occupies
exactly that role here. The alternatives — teaching the Hub to serve an SPA, or adding a
CORS policy — both widen a backend contract to solve a packaging problem, and would make
the Hub's browser-facing surface depend on where the frontend happens to be deployed.

**The frontend is a client-rendered SPA.** `@sveltejs/adapter-static` with an
`index.html` fallback, plus `ssr = false` in `src/routes/+layout.ts`. This is the one
choice here that constrains the application rather than the packaging: **no route may rely
on a server `load`, a `+server.ts`, form actions, or server-only environment.** Nothing does
today — `board/+page.ts` is a redirect and `tasks/[taskId]/+page.ts` threads a route param
— and every screen fetches over `fetch`/SignalR after mount, so SSR was rendering empty
shells. `@sveltejs/adapter-auto`, which this replaced, is a platform chooser that fails the
build on anything it does not recognize; a container is exactly that case.
*If a server `load` is ever wanted*: `bun add -d @sveltejs/adapter-node`, swap the import in
`vite.config.ts`, delete the `ssr = false` line, and run `node build` behind the same proxy.
One commit, no data migration, nothing in the Hub changes.

**The credential arrives as a mounted file, never as a service environment variable.**
ADR-004 scopes the token to the spawned agent process: the Hub reads `.env` and injects it
per spawn, so it never enters the Hub's own environment where every child would inherit it.
Putting `ANTHROPIC_AUTH_TOKEN` on the `hub` service would look like it works and would
quietly undo that, which is why the deployment-smoke workflow fails the build if a
credential-shaped variable appears in `compose.yaml` or `deploy/`.

**The agent runtimes are baked into the image, and the agent directory is not a volume.**
ADR-022 fixed that the Hub consumes build artifacts and never produces them, and that the
agent directory *is* the agent runtime — binaries and instruction files together. The image
build passes `-p:GrimoireAgentDir=/app/.grimoire/agents/`, the redirect that ADR documents,
so each agent's own `PublishAgentRuntime` target delivers its complete output. Keeping it in
the image rather than on a volume means the Hub and the agents it spawns are always the same
build.

**The writable roots are relocated instead of mounted over the defaults.** `DataDir`
defaults to `.grimoire` and the agent directory to `.grimoire/agents`, so a volume at
`/app/.grimoire` would shadow the agent runtimes the build just placed there and the Hub
would fail its `agent_dir` validation on first start. Pointing the three roots at
`/var/lib/grimoire/*` via `Grimoire__Paths__*` keeps mutable state and immutable artifact in
separate trees — which is what makes the image disposable and the volumes portable. The
environment tier is part of ADR-022's own precedence chain (CLI > environment >
`appsettings.json`), so `appsettings.json` remains the sole source of *defaults*.

**The telemetry dashboard is ADR-005's**, reused rather than re-decided. The Hub's OTLP
exporter is registered unconditionally, so without a receiver it would retry into the void.

### What enforces this

`.github/workflows/deploy-smoke.yml` builds the stack and exercises it — the stack serves
both halves through one origin, no credential-shaped variable appears in any deployment
file, and a missing secrets file fails the Hub closed. It is path-filtered rather than run
on every pull request, because a full image build duplicates what `ci.yml` already does;
the gap that leaves is a backend change breaking only the image build, which surfaces on the
next deployment-touching PR or a manual run.

### Known rough edges

- The proxy configuration encodes the route split between frontend and Hub. A new prefix
  mapped in `HubEndpoints` and not added to `deploy/Caddyfile` is a 404 no test can see.
- Each agent subfolder carries its own copy of the shared dependency set — the per-agent
  duplication ADR-022 accepted, now multiplied into an image layer.
- Rebuilding while the stack runs rewrites the directory agents are launched from.
  Rebuild-then-restart is the supported sequence.
