# Testing on a server

The development loop this sets up: you develop on your laptop and push a branch, then tell
a Claude Code session running **on the server** to put that ref live. It rebuilds
[the stack](../README.md), replaces the running containers, checks that the deployment
actually serves, and reports back. You look at the real thing over an SSH tunnel.

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
telemetry dashboard is deliberately unsecured — [`deploy/README.md`](../README.md) records
that as out of scope, and on a server "trusted host" has to mean something narrower than it
does on a laptop.

Tunnel in over SSH:

```bash
ssh -L 8080:127.0.0.1:8080 -L 18888:127.0.0.1:18888 server
```

Then open <http://localhost:8080> on your laptop, and the dashboard on
<http://localhost:18888>. Another private overlay network (WireGuard, say) works the same
way — bind to the interface it gives you with `GRIMOIRE_BIND`. Tailscale has a first-class
path below that needs no rebinding at all.

Only widen the binding once something in front of it terminates TLS and asks who you are:

```bash
GRIMOIRE_BIND=0.0.0.0 grimoire-server deploy main     # deliberate, not the default
```

### As a Tailscale Service

The other way in, and the one that gets you a real hostname without widening the binding
at all. `tailscale serve` runs **on the host**, terminates TLS for the service's own
MagicDNS name and proxies to `127.0.0.1` — the stack keeps publishing to loopback only,
and the tailnet's grants decide who reaches it.

```bash
export GRIMOIRE_TAILSCALE_SERVICE=svc:grimoire
grimoire-server deploy main
#   ==> Advertising svc:grimoire on :443 → 127.0.0.1:8080
#       tailnet   https://grimoire.crested-centauri.ts.net
```

Two preconditions belong to the tailnet, not to this script, and it can only tell you
about them:

1. **The service exists in the tailnet policy** with a `tcp:443` endpoint — admin console
   → *Services* → *Define a Service*. Its DNS name is not yours to choose; it is
   `<service>.<tailnet>.ts.net`, which is what `grimoire-server` prints.
2. **This host is a tagged node.** `tailscale serve` refuses to host a service from a node
   that authenticated as a user, so the deployment host needs `tailscale up --advertise-tags=...`
   and a grant that lets your users reach `svc:grimoire` on port 443.

Set nothing and none of this happens: with `GRIMOIRE_TAILSCALE_SERVICE` unset, every
command behaves exactly as it did before, and a host without `tailscale` installed is
unaffected.

The service follows the deployment's lifecycle, so the tailnet name never points at a
stack that is not serving:

| When | What happens to the service |
| --- | --- |
| `deploy`, after the images are built | drained — the old stack served until the containers went away |
| `deploy`, after all four smoke checks pass | configured and advertised |
| `deploy`, smoke failed | left drained; the stack still runs for diagnosis |
| `restart`, after its smoke checks pass | advertised again — this is how a drained stack comes back |
| `down` | drained before the containers stop |

Drive it by hand when you need to:

```bash
grimoire-server tailscale status      # the URL, and what this host serves for it
grimoire-server tailscale up          # advertise the running stack
grimoire-server tailscale drain       # stop taking new connections; config kept
grimoire-server tailscale off         # remove the service config from this host
```

The telemetry dashboard is deliberately **not** published this way. It has no
authentication of its own and `deploy/README.md` records securing it as out of scope; the
SSH tunnel above stays the way to reach it.

## The Claude Code session on the server

Remote Control connects claude.ai or the Claude app to a Claude Code process running on
your server, so "deploy pr/95" from your phone runs on the server's filesystem. Start it in
the checkout, inside `tmux` — the process must outlive your SSH connection:

```bash
grimoire-server tmux         # the `grimoire` session, in the deployment checkout
claude                       # once, to accept the workspace trust dialog and /login
claude remote-control --name grimoire-server
```

`grimoire-server tmux` is the whole tmux dance in one command: it attaches to the
`grimoire` session, and creates it in the deployment checkout if it is not there yet — so
the same command starts the session on Monday and finds it again on Friday. It takes a
session name (`grimoire-server tmux scratch`), reads `GRIMOIRE_TMUX_SESSION` for a
different default, and does the sensible thing when you are already inside tmux (switch
this client) or have no terminal to attach to at all — the Claude Code session running the
command itself gets told the session exists and how to reach it, rather than an error.

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
grimoire-server status [--no-fetch]        what is deployed, and what else is running
grimoire-server smoke                      re-run the endpoint checks
grimoire-server logs [service...]          tail container logs
grimoire-server restart [service...]       restart without rebuilding
grimoire-server down                       stop; the state volumes are kept
grimoire-server rollback                   redeploy the previously deployed commit
grimoire-server update [path]              fetch origin, refresh this script, then status
grimoire-server tailscale [action]         status · up · drain · off
grimoire-server tmux [session]             attach to the Claude Code session, or start it
grimoire-server install [path]             copy the script outside the checkout
grimoire-server version                    the tool, where it came from, what is deployed
```

Refs take any of `main`, `some/branch`, `v1.2.3`, `a703846`, and — the shape git does not
fetch by default — `pr/95`, `#95` or `pull/95/head`.

### `status` — everything the server is doing

It fetches first, so it answers the question this whole setup exists for: *is there
something new on the ref I am running?* It prints the new commits if there are any. Then it
reports the four things a server can be wrong in independently:

| Section | Answers |
| --- | --- |
| the deployment record | which ref and commit is deployed, when, and whether someone has moved the checkout by hand since |
| `Containers` | what `docker compose ps` sees |
| the smoke checks | whether the stack actually serves (the same four checks a deployment must pass) |
| `tmux` / tailnet service | whether the Claude Code session is still there, and whether the tailnet name points at this stack |

The last two are reported whether or not they are set up — "no `grimoire` session" and "no
tailnet service configured" are answers, and a server whose agent session quietly died
overnight looks perfectly healthy from every other section.

### `update` — the tool catches up, the stack does not

```console
$ grimoire-server update
==> Fetching origin
==> Updated /home/ops/.local/bin/grimoire-server — 0.9.0 → 1.0.0
    this invocation is still running the old copy; the next one is the new one
==> Deployed main — a703846e4573
    …
```

It fetches origin and then refreshes the copy `install` put in `~/.local/bin` from the one
in the checkout — and then runs `status`, without fetching a second time. It deliberately
**does not deploy**: moving the running stack to another commit stays an explicit `deploy`.

The copy is refreshed from the *checkout*, which is at the commit this host has deployed
and smoke-checked — so the tool you get is the tool this server has actually exercised. A
newer one sitting on `origin/main` is reported rather than installed; it arrives with the
deploy that brings the rest of that commit.

Two details that matter on a server: the copy is replaced by rename, never written in
place, so an `update` cannot truncate the script that is running it (which is also why the
invocation you typed finishes as the old version). And `install` and `update` both write
down the commit the copy came from, which is what `version` reports — a single file lifted
out of a checkout has no other way to say where it is from.

### `version` — three versions that are not the same thing

```console
$ grimoire-server version
==> grimoire-server 1.0.0
    script    /home/ops/.local/bin/grimoire-server
    copied    from 5bd8fa63a762 on 2026-08-17T11:36:31Z
    checkout  /srv/grimoire at 0.0.25-2-g5bd8fa6
    deployed  main — a703846e4573
```

The tool's own version is hand-maintained in the script's text: it is one file running
outside every checkout, so no build step can stamp it (ADR-027 explains why that is the one
exception, and what stamps everything else). The commit it was copied from, the checkout,
and the deployed commit are all recorded rather than inferred, and any of the four can be
out of step with the others — which is exactly why they are printed separately.

The stack has a version of its own: `deploy` builds it from the checkout and passes it
into the image, and the Hub prints it under its logo and answers with it on
`GET /api/version`:

```console
$ curl -s http://127.0.0.1:8080/api/version
{"version":"0.0.26-claude-frontend-batch-harness-wv1asv.31"}
```

```
0.0.26-claude-frontend-batch-harness-wv1asv.31
└ tag ┘ └──────────── branch ─────────────┘ └┘ commits since the tag
```

The branch and the commit count are dot-separated SemVer prerelease identifiers, so the
string parses and sorts as a prerelease of the next release — and it happens to match the
shape GitVersion would produce for the same branch, without GitVersion running here (the
image build has no `.git`, which is the whole reason the version travels in as a build
argument). Characters SemVer does not allow in an identifier become `-`, so
`claude/frontend-batch-harness-wv1asv` deploys as `claude-frontend-batch-harness-wv1asv`.

Four cases are decided rather than left to chance:

| Case | Version |
| --- | --- |
| On a branch, some commits past the tag | `0.0.26-main.31` — `main` is not special-cased to an empty label; this marks a deployment, not a release |
| Exactly on the tag | `0.0.26` — `0.0.26-main.0` would sort *before* the release it in fact is |
| `--force` with local changes | `0.0.26-main.31+dirty` — build metadata, so it does not affect ordering |
| `rollback`, which re-deploys a bare sha | `0.0.26-g<short sha>.31` — there is no branch to name |
| An all-digit branch name with a leading zero, e.g. `001` | `0.0.26-ref-001.31` — SemVer forbids leading zeros on a numeric identifier; `0` and `42` are valid and stay as they are |

With no tags in the checkout at all the version is `0.0.0-unknown`: an unversioned image
still deploys.

### What a deployment checks before it believes itself

Four things, against the running stack, in the order a broken deployment tends to fail:

| Check | Breaks when |
| --- | --- |
| `/` returns the SPA document | the frontend build is wrong, or the Hub did not mount it |
| `/api/board` answers | the Hub is down, or failed to start |
| `/tasks/does-not-exist` falls back to the SPA | deep links 404 instead of reaching the router |
| `/hubs/ingest-lifecycle/negotiate` offers WebSockets | the realtime transport is broken |

All four hit the Hub directly — it serves the UI, the API and the SignalR hubs on one
origin, so there is nothing between them and the application. The first three overlap
`.github/workflows/deploy-smoke.yml`; the fourth is the one CI omits.

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
| `GRIMOIRE_VERSION` | `<tag>-<branch>.<commits>` for the deployed ref | the version stamped into the image (ADR-027); set it to override what a deployed Hub reports for itself |
| `GRIMOIRE_TAILSCALE_SERVICE` | unset | tailnet service to advertise, e.g. `svc:grimoire`; unset turns the feature off |
| `GRIMOIRE_TAILSCALE_PORT` | `443` | HTTPS port of the service endpoint; rejected before an image build if it is not a port |
| `GRIMOIRE_TAILSCALE_DOMAIN` | derived | overrides the derived `<service>.<tailnet>.ts.net` |
| `GRIMOIRE_TMUX_SESSION` | `grimoire` | tmux session `grimoire-server tmux` attaches to |

## When something is wrong

- **`the deployment checkout has local changes`** — the checkout drifted from origin.
  `git -C ~/grimoire status` will say how; this host is meant to run what is on origin.
- **`... has no compose.yaml — it predates the deployment stack`** — the ref is older than
  the deployment stack. Nothing was deployed and the running stack is untouched.
- **`no such pull request head on origin`** — the PR number is wrong, or the PR is from a
  fork whose head is not on this remote.
- **`cannot talk to the docker daemon`** — the daemon is down, or your user is not in the
  `docker` group (`newgrp docker` after adding it).
- **Smoke fails on the SignalR check only** — the realtime upgrade is not getting through.
  If you put anything in front of the stack, check that it passes WebSocket upgrades;
  otherwise `grimoire-server logs hub`.
- **The Hub restarts in a loop** — nearly always `.env`: absent, empty, or unreadable by
  the container's uid. The message names `secrets_file`.
- **`tailscale serve failed`** — usually one of the two tailnet preconditions: the service
  is not defined in the policy with a `tcp:443` endpoint, or this host authenticated as a
  user instead of a tag (`service hosts must be tagged nodes`).
  `grimoire-server tailscale status` shows what this host currently serves.
- **The tailnet name resolves but refuses the connection** — the host is drained. That is
  what `down`, and a `deploy` whose smoke checks failed, leave behind.
  `grimoire-server tailscale up` re-advertises a stack you have since satisfied yourself
  about.

## Tests

```bash
./deploy/server/grimoire-server.test.sh
```

Covers what this script itself decides — ref-specification parsing, the Compose version
gate, the deployment state file, the overlay seeding rule, the deployed image's version
string, and the tailnet service name, port and URL it derives from the environment — and
runs in
`.github/workflows/deploy-smoke.yml`. It starts no containers, runs no `tailscale`, and
reaches no network: whether `tailscale serve` then works is tailscale's contract, not
this script's.
