# Testing on a server

The development loop this sets up: you develop on your laptop and push a branch, then tell
a Claude Code session running **on the server** to put that ref live. It rebuilds
[ADR-027's stack](../../docs/adr/ADR-027-container-image-and-deployment-topology.md),
replaces the running containers, checks that the deployment actually serves, and reports
back. You look at the real thing over an SSH tunnel.

One host, one stack, one ref at a time. `deploy` is the only command that changes what is
running, and the three state volumes are never touched by it — the wiki survives every
deployment, every rollback and every ref switch.

```
laptop                     GitHub                     server
  push  ────────────────▶  branch / PR
                                   ▲                    │ grimoire-server deploy pr/95
  "deploy pr/95" ─── Remote Control ─────────────────▶  │ fetch → checkout → build → up → smoke
  browser ◀───── ssh -L 8080 ───────────────────────────┘ http://127.0.0.1:8080
```

Nothing here polls GitHub or deploys on its own. Deployments happen when you ask for one —
`grimoire-server status` is how you find out whether there is something new to ask for.

## One-time setup on the server

**1. A container runtime with Compose 2.24 or newer.** The overlay below uses the
`!override` merge tag, which Compose added in 2.24.

```bash
docker compose version --short
```

**2. A checkout that belongs to the deployment**, not one you also edit by hand.
`deploy` refuses to move a dirty tree, and it leaves the checkout on a detached HEAD.

```bash
git clone https://github.com/wmalgadey/grimoire.git ~/grimoire
cd ~/grimoire
```

**3. The credential.** `.env` is the Hub's entire configuration surface and a required
input — without it the Hub fails closed at startup naming `secrets_file`. It is mounted
read-only into the container and never reaches the Hub's own environment (ADR-004); see
[`deploy/README.md`](../README.md).

```bash
cp .env-example .env
chmod 600 .env          # it holds your Anthropic token
$EDITOR .env
```

**4. Put the command somewhere the checkout cannot take it away.** `deploy` checks out
other refs — including refs older than this tooling — so the copy you invoke should live
outside the working tree:

```bash
./deploy/server/grimoire-server install
echo 'export GRIMOIRE_REPO=$HOME/grimoire' >> ~/.bashrc
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bashrc
```

The same reasoning applies to the compose overlay: `grimoire-server` copies
`compose.server.yaml` into its state directory on first use and runs from that copy, so a
ref without the file still gets the server's port binding.

**5. First deployment.**

```bash
grimoire-server deploy main
```

The first build is slow — the .NET SDK, Bun and the three agent builds all run — and every
later one reuses the layer cache. When it finishes you get the smoke results and the URLs.

Containers carry `restart: unless-stopped`, so the stack comes back by itself after a
reboot, on whatever ref was last deployed.

## Reaching it from your laptop

**The stack publishes to `127.0.0.1` only.** It has no TLS and no authentication, and its
telemetry dashboard is deliberately unsecured — ADR-027 records that as out of scope, and
on a server "trusted host" has to mean something narrower than it does on a laptop.

Tunnel in over SSH:

```bash
ssh -L 8080:127.0.0.1:8080 -L 18888:127.0.0.1:18888 server
```

Then open <http://localhost:8080> on your laptop, and the dashboard on
<http://localhost:18888>. A private overlay network (Tailscale, WireGuard) works the same
way — bind to the interface it gives you with `GRIMOIRE_BIND`.

Only widen the binding once something in front of it terminates TLS and asks who you are:

```bash
GRIMOIRE_BIND=0.0.0.0 grimoire-server deploy main     # deliberate, not the default
```

## The Claude Code session on the server

Remote Control connects claude.ai or the Claude app to a Claude Code process running on
your server, so "deploy pr/95" from your phone runs on the server's filesystem. Start it in
the checkout, inside `tmux` — the process must outlive your SSH connection:

```bash
tmux new -s grimoire
cd ~/grimoire
claude                       # once, to accept the workspace trust dialog and /login
claude remote-control --name grimoire-server
```

Then detach (`Ctrl-b d`) and pick the session up from the session list at
[claude.ai/code](https://claude.ai/code) or in the Claude app. Requirements and the full
flag list are in the [Remote Control docs](https://code.claude.com/docs/en/remote-control):
it needs a Pro/Max/Team/Enterprise login through `/login`, an API key does not work, and
`ANTHROPIC_BASE_URL` must be unset or point at `api.anthropic.com`.

That login is a different credential from the one in `.env`. `.env` holds the token
Grimoire's *agents* spawn with; the Claude Code session on the server authenticates as
you. Neither belongs in git.

The session has the [`deploy-server`](../../.claude/skills/deploy-server/SKILL.md) skill,
which is what makes a one-line instruction enough:

> deploy pr/95 and check whether the board renders

To stop approving the same command every time, allow it in the server checkout's
`.claude/settings.local.json` (git-ignored, server-only):

```json
{
  "permissions": {
    "allow": [
      "Bash(grimoire-server:*)",
      "Bash(~/.local/bin/grimoire-server:*)"
    ]
  }
}
```

## Commands

```
grimoire-server deploy [<ref>] [--force]   check out a ref, rebuild, replace the stack
grimoire-server status [--no-fetch]        what is deployed, what origin has since
grimoire-server smoke                      re-run the endpoint checks
grimoire-server logs [service...]          tail container logs
grimoire-server restart [service...]       restart without rebuilding
grimoire-server down                       stop; the state volumes are kept
grimoire-server rollback                   redeploy the previously deployed commit
```

Refs take any of `main`, `some/branch`, `v1.2.3`, `a703846`, and — the shape git does not
fetch by default — `pr/95`, `#95` or `pull/95/head`.

`status` fetches first, so it answers the question this whole setup exists for: *is there
something new on the ref I am running?* It prints the new commits if there are any.

### What a deployment checks before it believes itself

Four things, against the running stack, in the order a broken deployment tends to fail:

| Check | Breaks when |
| --- | --- |
| `/` returns the SPA document | the frontend build or the proxy's file server is wrong |
| `/api/board` answers through the proxy | the Hub is down, or `/api` is not forwarded |
| `/tasks/does-not-exist` falls back to the SPA | deep links 404 instead of reaching the router |
| `/hubs/ingest-lifecycle/negotiate` offers WebSockets | the proxy drops the realtime upgrade |

The first three are what `.github/workflows/deploy-smoke.yml` verifies for ADR-027's V1;
the fourth is the one CI omits and a proxy misconfiguration breaks first.

A failing deployment is **left running**. It is usually the thing you wanted to look at,
and tearing it down would take the evidence with it — `grimoire-server logs` and
`grimoire-server rollback` are both one command away.

## State, and how to throw it away

Three named volumes, exactly as `deploy/README.md` describes: `grimoire-wiki` (the
product — back this one up), `grimoire-data`, `grimoire-memory`. Deployments and rollbacks
never touch them; images are disposable, state is not.

Point the Hub at host directories instead by setting `GRIMOIRE_WIKI_DIR`,
`GRIMOIRE_DATA_DIR` or `GRIMOIRE_MEMORY_DIR` in `.env`, with `GRIMOIRE_UID` set to the
owning user — see [`deploy/README.md`](../README.md#pointing-the-hub-at-your-own-directories).

Starting over is deliberately not a flag on `down`:

```bash
docker compose --project-directory ~/grimoire -f ~/grimoire/compose.yaml down --volumes
```

## Configuration

All of it is environment, all of it optional:

| Variable | Default | |
| --- | --- | --- |
| `GRIMOIRE_REPO` | the checkout the script sits in | the deployment checkout |
| `GRIMOIRE_STATE_DIR` | `$XDG_STATE_HOME/grimoire-server` | deployment record and the compose overlay |
| `GRIMOIRE_BIND` | `127.0.0.1` | address the published ports bind to |
| `GRIMOIRE_PORT` | `8080` | application port |
| `GRIMOIRE_DASHBOARD_PORT` | `18888` | telemetry dashboard port |
| `GRIMOIRE_HEALTH_TIMEOUT` | `180` | seconds to wait for the stack to answer |
| `GRIMOIRE_LOG_LINES` | `100` | lines `logs` tails |

## When something is wrong

- **`the deployment checkout has local changes`** — the checkout drifted from origin.
  `git -C ~/grimoire status` will say how; this host is meant to run what is on origin.
- **`... has no compose.yaml — it predates the deployment stack`** — the ref is older than
  ADR-027. Nothing was deployed and the running stack is untouched.
- **`no such pull request head on origin`** — the PR number is wrong, or the PR is from a
  fork whose head is not on this remote.
- **`cannot talk to the docker daemon`** — the daemon is down, or your user is not in the
  `docker` group (`newgrp docker` after adding it).
- **Smoke fails on the SignalR check only** — the proxy is not passing the WebSocket
  upgrade. `grimoire-server logs proxy`.
- **The Hub restarts in a loop** — nearly always `.env`: absent, empty, or unreadable by
  the container's uid. The message names `secrets_file`.

## Tests

```bash
./deploy/server/grimoire-server.test.sh
```

Covers what this script itself decides — ref-specification parsing, the Compose version
gate, the deployment state file, the overlay seeding rule — and runs in
`.github/workflows/deploy-smoke.yml`. It starts no containers and reaches no network.
