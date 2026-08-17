# Running Grimoire

A self-hosted stack: the Hub, the three agent runtimes, the frontend, and a local
telemetry dashboard. The decision behind its shape is
[ADR-027](../docs/adr/ADR-027-container-image-and-deployment-topology.md).

You need a container runtime with Compose v2. You do **not** need the .NET SDK, Bun, or a
prior `dotnet build` — the image builds and carries the agent runtimes itself.

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

State lives in three named volumes:

- `grimoire-wiki` — **the product**. Back this one up.
- `grimoire-data` — operational-state database and raw sources.
- `grimoire-memory` — task, conversation, finding and remediation records.

The agent directory is *not* a volume. It is a build artifact inside the image, so the Hub
and the agents it spawns are always the same build.

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

## Before you expose this

This stack is built for a trusted host. It has **no TLS, no authentication**, and an
unsecured telemetry dashboard, and the Hub container runs as root. Putting it on an
untrusted network needs work this deployment does not do — ADR-027 records that as out of
scope rather than done.
