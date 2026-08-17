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

A second wrinkle if you edit `.env` while the stack runs: it is bind-mounted as a *file*,
so the container follows the inode, not the name. `sed -i` and most editors write a new
file and rename it over the old one, which leaves the Hub reading the original forever.
Truncate in place instead — `cat new-env > .env` — or recreate the container.

### Model overrides, and what a 429 really means

Each agent reads its **own** model variable — `GRIMOIRE_INGEST_MODEL`,
`GRIMOIRE_QUERY_MODEL`, `GRIMOIRE_LINT_MODEL`. ADR-004 keeps their scopes independent, and
that independence goes further than it first looks: they do not inherit from one another,
and an unset one does not quietly borrow a sibling's value. It falls back to
`AnthropicModelClient`'s hardcoded default. Setting only `GRIMOIRE_INGEST_MODEL` therefore
leaves Query and Lint on a model nobody chose.

Getting that model wrong does not present as a configuration error. An OAuth-style
credential — `sk-ant-oat…`, as opposed to a classic `sk-ant-api…` key — answers a request
for a model it is not entitled to with

```
Model API error 429 (rate_limit_error): Error
```

A rate limit, in other words, for a request that was never rate limited. Grimoire is
reporting faithfully; the provider is what says almost nothing. Before going looking for
load or backoff, check entitlement directly:

```bash
curl -sS -D - -o /dev/null https://api.anthropic.com/v1/messages \
  -H "Authorization: Bearer $ANTHROPIC_AUTH_TOKEN" \
  -H "anthropic-beta: oauth-2025-04-20" \
  -H "anthropic-version: 2023-06-01" -H "content-type: application/json" \
  -d '{"model":"claude-haiku-4-5","max_tokens":4,"messages":[{"role":"user","content":"hi"}]}'
```

A 200 carries `anthropic-ratelimit-unified-*` headers reporting the real utilization of the
5-hour and 7-day windows. That is what separates the two cases: a genuine rate limit shows
up there as a high number, an entitlement gap as a 429 carrying no such headers at all. Use
`Authorization: Bearer` plus the beta header for an `oat` token — `x-api-key` returns a bare
401 for it and tells you nothing. Swap the model in that payload to map which ones the
credential actually covers.

Worth knowing before the first 429 rather than after it: nothing in the harness retries or
backs off. The turn fails, the record is written with its `failure_reason`, and the run is
over.

## What runs

| Service | Port | Notes |
| --- | --- | --- |
| `hub` | 8080 | The whole application: the UI, the API, the SignalR hubs and the three agent runtimes, on one origin. |
| `dashboard` | 18888 | Traces, metrics and logs (ADR-005). Holds no credential. |

There is no proxy in this stack. The Hub serves the frontend itself, so if you want TLS, a
hostname or authentication, put your own terminator in front of port 8080 — it needs no
Grimoire-specific configuration, only one upstream. On a cluster that is an Ingress.

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

The Hub runs rootless as uid `1654` by default, so without this the pages,
`log.md` entries and records the Hub writes into your vault belong to `1654` rather than
to you. Point `GRIMOIRE_UID` at your own id and `chown` the directory to yourself once,
and everything the Hub creates is yours.

Leave `GRIMOIRE_GID` alone. The image's writable roots are owned by group 0 and are
group-writable, which is what lets *any* uid write to them — change the group and a
managed volume stops being writable.

One more thing when you set it: `.env` has to be readable by that id, or the Hub fails
closed naming `secrets_file`.

**Changing `GRIMOIRE_UID` on a stack that has already run needs one more step.** The
group-0 convention above covers the three roots the image creates — it does not reach what
the Hub writes underneath them afterwards. `state/`, `raw/`, `write-locks/` and
`operational-state.db` come into existence at runtime under the process umask, so they end
up writable by their owner alone, and that owner is whichever uid was running at the time.
Point `GRIMOIRE_UID` somewhere new and the Hub crash-loops before it serves a single
request:

```
SQLite Error 8: 'attempt to write a readonly database'
```

Re-own the existing contents once, to the new uid and the same group 0 (`docker volume ls`
gives you the prefixed name — it is the project directory, so `grimoire_grimoire-data` from
a checkout called `grimoire`):

```bash
docker run --rm -u 0:0 -v grimoire_grimoire-data:/d alpine \
  sh -c 'chown -R 1000:0 /d && chmod -R g=u /d'
```

This bites hardest when you rebind only *some* of the three roots. The directories you
moved out to host paths already belong to you, which is the whole point of setting the uid;
the managed volume you deliberately left alone is the one that stops being writable, and it
is the one holding the database the Hub opens first.

## Security posture

Rootless by default, on terms meant to survive the move to a cluster:

- The Hub image declares a **numeric non-root uid** (`1654`). Numeric rather than a
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

## Running it on a server instead

Everything above assumes the machine you are sitting at. A host you deploy *to* — where
the ref changes as branches and pull requests come in, and where the published ports face
a network — has [`deploy/server/`](server/README.md): one command that puts a given ref
live and proves it serves, a compose overlay that binds the published ports to the
loopback address and caps container logs, and a skill that lets a Claude Code session on
that host do it on request.

```bash
grimoire-server deploy pr/95
grimoire-server status
```

## Why it is shaped this way

The rationale, kept here because each choice is a consequence of a decision already
recorded elsewhere rather than a new decision of its own.

**The Hub serves the frontend, so the deployment is one container.** The frontend addresses
the API and the SignalR hubs with relative paths; in development the Vite proxy in
`frontend/vite.config.ts` is what makes them same-origin. Rather than reproduce that with a
proxy of our own, `HubEndpoints.MapSingleOriginFrontend` mounts the built SPA from
`wwwroot/` beside the Hub assembly. Same-origin becomes true by construction, no CORS policy
is needed, and any proxy in front is ordinary infrastructure with a single upstream instead
of something that has to mirror Grimoire's route layout.

It is opt-in on the directory existing, so a source checkout — where `bun run dev` serves
the frontend — is unaffected. The load-bearing detail is that `MapFallbackToFile` catches
*every* unmatched path: without the explicit `/api/{**rest}` and `/hubs/{**rest}` fallbacks
beside it, a mistyped API path would answer the SPA document with HTTP 200 and every caller
checking status codes would read that as success. `HubFrontendHostingTests` pins both halves
— deep links reach the SPA, unmatched backend paths stay 404 — and was probed by removing
the guards and watching it fail.

**The frontend is a client-rendered SPA.** `@sveltejs/adapter-static` with an
`index.html` fallback, plus `ssr = false` in `src/routes/+layout.ts`. This is the one
choice here that constrains the application rather than the packaging: **no route may rely
on a server `load`, a `+server.ts`, form actions, or server-only environment.** Nothing does
today — `board/+page.ts` is a redirect and `tasks/[taskId]/+page.ts` threads a route param
— and every screen fetches over `fetch`/SignalR after mount, so SSR was rendering empty
shells. `@sveltejs/adapter-auto`, which this replaced, is a platform chooser that fails the
build on anything it does not recognize; a container is exactly that case.
*If a server `load` is ever wanted*: `bun add -d @sveltejs/adapter-node`, swap the import in
`vite.config.ts`, and delete the `ssr = false` line — the Node server then replaces the
static mount rather than sitting behind it.
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

**The image is told its own version; it cannot work it out.** GitVersion computes every
version number this repository produces, from the git history and the tags (ADR-027) — and
the build context has no history to read, because `.dockerignore` excludes `.git/` to keep
the context small and the layer cache from invalidating on every commit. So the version
comes in as the `GRIMOIRE_VERSION` build argument, `backend/Directory.Build.props` detects
the absent repository and steps aside, and the Hub prints what it was given under its logo
on every help screen. `grimoire-server deploy` sets it from the checkout it is deploying; a
bare `docker compose up --build` gets `0.0.0-local`, which is honest about being a local
build rather than pretending to be a release.

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

- Each agent subfolder carries its own copy of the shared dependency set — the per-agent
  duplication ADR-022 accepted, now multiplied into an image layer.
- Rebuilding while the stack runs rewrites the directory agents are launched from.
  Rebuild-then-restart is the supported sequence.
